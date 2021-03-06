use std::num::NonZeroUsize;

use futures_util::{
    future::{self, Either},
    pin_mut,
};
use mqtt3::ShutdownError;
use tracing::{debug, error, info, info_span};
use tracing_futures::Instrument;

use mqtt_util::Credentials;

use crate::{
    client::{ClientError, MqttClientConfig},
    config_update::BridgeDiff,
    persist::{PersistError, PublicationStore, RingBuffer, StreamWakeableState, WakingMemoryStore},
    pump::{Builder, Pump, PumpError, PumpHandle, PumpMessage},
    settings::{ConnectionSettings, MemorySettings, RingBufferSettings},
    upstream::{
        ConnectivityError, ConnectivityState, LocalUpstreamMqttEventHandler,
        LocalUpstreamPumpEvent, LocalUpstreamPumpEventHandler, RemoteUpstreamMqttEventHandler,
        RemoteUpstreamPumpEvent, RemoteUpstreamPumpEventHandler, RpcError,
    },
};

pub struct BridgeHandle {
    local_pump_handle: PumpHandle<LocalUpstreamPumpEvent>,
    remote_pump_handle: PumpHandle<RemoteUpstreamPumpEvent>,
}

impl BridgeHandle {
    pub fn new(
        local_pump_handle: PumpHandle<LocalUpstreamPumpEvent>,
        remote_pump_handle: PumpHandle<RemoteUpstreamPumpEvent>,
    ) -> Self {
        Self {
            local_pump_handle,
            remote_pump_handle,
        }
    }

    pub async fn send_update(&mut self, message: BridgeDiff) -> Result<(), BridgeError> {
        let (local_updates, remote_updates) = message.into_parts();

        if local_updates.has_updates() {
            debug!("sending update to local pump {:?}", local_updates);
            self.local_pump_handle
                .send(PumpMessage::ConfigurationUpdate(local_updates))
                .await?;
        }

        if remote_updates.has_updates() {
            debug!("sending update to remote pump {:?}", remote_updates);
            self.remote_pump_handle
                .send(PumpMessage::ConfigurationUpdate(remote_updates))
                .await?;
        }

        Ok(())
    }

    pub async fn shutdown(mut self) {
        if let Err(e) = self.local_pump_handle.send(PumpMessage::Shutdown).await {
            error!(error = %e, "unable to request shutdown for local pump");
        }

        if let Err(e) = self.remote_pump_handle.send(PumpMessage::Shutdown).await {
            error!(error = %e, "unable to request shutdown for remote pump");
        }
    }
}

/// Bridge implementation that connects to local broker and remote broker and handles messages flow
pub struct Bridge<S> {
    local_pump: Pump<S, LocalUpstreamMqttEventHandler<S>, LocalUpstreamPumpEventHandler>,
    remote_pump: Pump<S, RemoteUpstreamMqttEventHandler<S>, RemoteUpstreamPumpEventHandler>,
}

impl Bridge<WakingMemoryStore> {
    pub fn new_upstream(
        system_address: &str,
        device_id: &str,
        settings: &ConnectionSettings,
        memory_settings: MemorySettings,
    ) -> Result<Self, BridgeError> {
        const BATCH_SIZE: usize = 10;

        debug!("creating bridge {}...", settings.name());

        let (local_pump, remote_pump) = Builder::<WakingMemoryStore>::default()
            .with_local(|pump| {
                pump.with_config(MqttClientConfig::new(
                    system_address,
                    settings.keep_alive(),
                    settings.clean_session(),
                    Credentials::Anonymous(format!("{}/{}/$bridge", device_id, settings.name())),
                ))
                .with_rules(settings.forwards());
            })
            .with_remote(|pump| {
                pump.with_config(MqttClientConfig::new(
                    settings.address(),
                    settings.keep_alive(),
                    settings.clean_session(),
                    settings.credentials().clone(),
                ))
                .with_rules(settings.subscriptions());
            })
            .with_store(move |_| {
                Ok(PublicationStore::new_memory(
                    NonZeroUsize::new(BATCH_SIZE).unwrap(),
                    &memory_settings,
                ))
            })
            .build()?;

        debug!("created bridge {}...", settings.name());

        Ok(Bridge {
            local_pump,
            remote_pump,
        })
    }
}

impl Bridge<RingBuffer> {
    pub fn new_upstream(
        system_address: &str,
        device_id: &str,
        settings: &ConnectionSettings,
        ring_buffer_settings: RingBufferSettings,
    ) -> Result<Self, BridgeError> {
        const BATCH_SIZE: usize = 10;

        debug!("creating bridge {}...", settings.name());
        let bridge_name = String::from(settings.name());

        let (local_pump, remote_pump) = Builder::<RingBuffer>::default()
            .with_local(|pump| {
                pump.with_config(MqttClientConfig::new(
                    system_address,
                    settings.keep_alive(),
                    settings.clean_session(),
                    Credentials::Anonymous(format!("{}/{}/$bridge", device_id, settings.name())),
                ))
                .with_rules(settings.forwards());
            })
            .with_remote(|pump| {
                pump.with_config(MqttClientConfig::new(
                    settings.address(),
                    settings.keep_alive(),
                    settings.clean_session(),
                    settings.credentials().clone(),
                ))
                .with_rules(settings.subscriptions());
            })
            .with_store(move |suffix| {
                PublicationStore::new_ring_buffer(
                    NonZeroUsize::new(BATCH_SIZE).unwrap(),
                    &ring_buffer_settings,
                    &bridge_name,
                    suffix,
                )
            })
            .build()?;

        debug!("created bridge {}...", settings.name());

        Ok(Bridge {
            local_pump,
            remote_pump,
        })
    }
}

impl<S> Bridge<S>
where
    S: StreamWakeableState + Send,
{
    pub async fn run(self) -> Result<(), BridgeError> {
        info!("starting bridge...");

        let mut local_pump_handle = self.local_pump.handle();
        let local_pump = self
            .local_pump
            .run()
            .instrument(info_span!("pump", name = "local"));

        // Send initial state as disconnected
        local_pump_handle
            .send(PumpMessage::Event(
                LocalUpstreamPumpEvent::ConnectivityUpdate(ConnectivityState::Disconnected),
            ))
            .await?;

        let shutdown_remote_pump = self.remote_pump.handle();
        let remote_pump = self
            .remote_pump
            .run()
            .instrument(info_span!("pump", name = "remote"));

        debug!("starting pumps ...",);

        pin_mut!(local_pump, remote_pump);

        match future::select(local_pump, remote_pump).await {
            Either::Left((local_pump, remote_pump)) => {
                if let Err(e) = local_pump {
                    error!(error = %e, "local pump exited with error");
                } else {
                    info!("local pump exited");
                }

                debug!("shutting down remote pump...");
                shutdown_remote_pump.shutdown().await;

                if let Err(e) = remote_pump.await {
                    error!(error = %e, "remote pump exited with error");
                } else {
                    info!("remote pump exited");
                }
            }
            Either::Right((remote_pump, local_pump)) => {
                if let Err(e) = remote_pump {
                    error!(error = %e, "remote pump exited with error");
                } else {
                    info!("remote pump exited");
                }

                debug!("shutting down local pump...");
                local_pump_handle.shutdown().await;

                if let Err(e) = local_pump.await {
                    error!(error = %e, "local pump exited with error");
                } else {
                    info!("local pump exited");
                }
            }
        }

        info!("bridge stopped");
        Ok(())
    }

    pub fn handle(&self) -> BridgeHandle {
        BridgeHandle::new(self.local_pump.handle(), self.remote_pump.handle())
    }
}

/// Bridge error.
#[derive(Debug, thiserror::Error)]
pub enum BridgeError {
    #[error("failed to save to store. Caused by: {0}")]
    Store(#[from] PersistError),

    #[error("failed to subscribe to topic. Caused by: {0}")]
    Subscribe(#[source] ClientError),

    #[error("failed to parse topic pattern. Caused by: {0}")]
    TopicFilterParse(#[from] mqtt_broker::Error),

    #[error("failed to load settings. Caused by: {0}")]
    LoadingSettings(#[from] config::ConfigError),

    #[error("Failed to get send pump message.")]
    SendToPump,

    #[error("Failed to send message to pump: {0}")]
    SendBridgeUpdate(#[from] PumpError),

    #[error("failed to execute RPC command. Caused by: {0}")]
    Rpc(#[from] RpcError),

    #[error("failed to execute connectivity event. Caused by: {0}")]
    Connectivity(#[from] ConnectivityError),

    #[error("failed to signal bridge shutdown.")]
    ShutdownBridge,

    #[error("failed to get publish handle from client. Caused by: {0}")]
    PublishHandle(#[source] ClientError),

    #[error("failed to validate client settings. Caused by: {0}")]
    ValidationError(#[source] ClientError),

    #[error("failed to get subscribe handle from client. Caused by: {0}")]
    UpdateSubscriptionHandle(#[source] ClientError),

    #[error("failed to get publish handle from client. Caused by: {0}")]
    ClientShutdown(#[from] ShutdownError),

    #[error("storage not set")]
    UnsetStorage,
}
