// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Core.Test.Config
{
    using System.Collections.Generic;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Config;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using Xunit;

    public class TwinConfigSourceTest
    {
        [Unit]
        [Theory]
        [MemberData(nameof(GetTwinCollectionForIntegrityCheck))]
        public void TestCheckIfTwinPropertiesAreSigned(bool expectedResult, TwinCollection twinDesiredProperties)
        {
            Assert.Equal(expectedResult, TwinConfigSource.CheckIfTwinPropertiesAreSigned(twinDesiredProperties));
        }

        public static IEnumerable<object[]> GetTwinCollectionForIntegrityCheck()
        {
            var integrity = new
            {
                header = new
                {
                    signercert = new string[] { "signercert1", "signercert2" },
                    intermediatecacert = new string[] { "intermediatecacert1", "intermediatecacert2" }
                },
                signature = new
                {
                    bytes = "signature",
                    algorithm = "algo"
                }
            };

            TwinCollection twinDesiredPropertiesWithoutIntegrity = GetTwinDesiredProperties(null);
            TwinCollection twinDesiredPropertiesWithIntegrity = GetTwinDesiredProperties(integrity);
            yield return new object[] { true, twinDesiredPropertiesWithIntegrity };
            yield return new object[] { false, twinDesiredPropertiesWithoutIntegrity };
        }

        static TwinCollection GetTwinDesiredProperties(object integrity)
        {
            var desiredProperties = new
            {
                routes = new
                {
                    route = "FROM /messages/* INTO $upstream"
                },
                schemaVersion = "1.1",
                storeAndForwardConfiguration = new
                {
                    timeToLiveSecs = 7200
                },
                integrity,
                version = "10"
            };

            return new TwinCollection(JsonConvert.SerializeObject(desiredProperties));
        }

        [Unit]
        [Theory]
        [MemberData(nameof(GetTwinForVerifyingSignatures))]
        public void TestExtractHubTwinAndVerify(string type, string schemaVersion, string algo, string[] signercert, string[] intermediatecacert, string signature)
        {
            var desiredProperties = new
            {
                routes = new
                {
                    route = "FROM /messages/* INTO $upstream"
                },
                schemaVersion,
                storeAndForwardConfiguration = new
                {
                    timeToLiveSecs = 7200
                },
                integrity = new
                {
                    header = new
                    {
                        signercert,
                        intermediatecacert,
                    },
                    signature = new
                    {
                        bytes = signature,
                        algorithm = algo
                    }
                },
                version = "10"
            };

            TwinCollection twinDesiredProperties = new TwinCollection(JsonConvert.SerializeObject(desiredProperties));
            if (type == "good")
            {
                Assert.True(TwinConfigSource.ExtractHubTwinAndVerify(twinDesiredProperties));
            }
            else
            {
                Assert.False(TwinConfigSource.ExtractHubTwinAndVerify(twinDesiredProperties));
            }
        }

        public static IEnumerable<object[]> GetTwinForVerifyingSignatures()
        {
            string goodSchemaVersion = "1.1";
            string badSchemaVersion = "10000.0";
            string[] ecdsaSignerCert = GetEcdsaSignerTestCert();
            string[] ecdsaIntermediateCACert = GetEcdsaIntermediateCATestCert();
            string ecdsaSignature = GetEcdsaTestEdgeHubSignature();
            string ecdsaAlgo = "ES256";

            string[] rsaSignerCert = GetRsaSignerTestCert();
            string[] rsaIntermediateCACert = GetRsaIntermediateCATestCert();
            string rsaSignature = GetRsaTestEdgeHubSignature();
            string rsaAlgo = "RS256";

            yield return new object[] { "good", goodSchemaVersion, ecdsaAlgo, ecdsaSignerCert, ecdsaIntermediateCACert, ecdsaSignature };
            yield return new object[] { "good", goodSchemaVersion, rsaAlgo, rsaSignerCert, rsaIntermediateCACert, rsaSignature };
            yield return new object[] { "bad", badSchemaVersion, rsaAlgo, rsaSignerCert, rsaIntermediateCACert, rsaSignature };
            yield return new object[] { "bad", badSchemaVersion, ecdsaAlgo, ecdsaSignerCert, ecdsaIntermediateCACert, ecdsaSignature };
        }

        public static string[] GetEcdsaSignerTestCert() => new string[]
            {
                "\rMIICOTCCAd+gAwIBAgICEAAwCgYIKoZIzj0EAwIwVDELMAkGA1UEAwwCc3MxCzAJ\rBgNVBAgMAldBMQswCQYDVQQGEwJVUzERMA8GCSqGSIb3DQEJARYCc3MxCzAJBgNV\rBAoMAnNzMQswCQYDVQQLDAJzczAeFw0xNTA1MDUwMDAwMDBaFw0yNDEyMzEyMzU5\rNTlaMFQxCzAJBgNVBAMMAnNzMQswCQYDVQQIDAJXQTELMAkGA1UEBhMCVVMxETAP\rBgkqhkiG9w0BCQEWAnNzMQswCQYDVQQKDAJzczELMAkGA1UECwwCc3MwWTATBgcq\rhkjOPQIBBggqhkjOPQMBBwNCAAS7kA6viM5eN1Y/E+1KUOjLEZdhsygtbntGqV",
                "7s\rMXG5ZEKr+drie2i6lMa8zu/hvHhOdbXiFVOZT045AYaGWBDRo4GgMIGdMAwGA1Ud\rEwEB/wQCMAAwHQYDVR0OBBYEFK0CsUii+1a5RlE+2aQMKrxwlFkeMB8GA1UdIwQY\rMBaAFI3svRm8zDySNcXiJCaqn6phhFtPMAsGA1UdDwQEAwIBpjATBgNVHSUEDDAK\rBggrBgEFBQcDATArBgNVHREEJDAigg9hLmEuZXhhbXBsZS5jb22CD2IuYi5leGFt\rcGxlLmNvbTAKBggqhkjOPQQDAgNIADBFAiBWXuB2+R1lXV3HPmmu7eJc3H2rpr8o\rKwR8wnDdnuYL+AIhAIM5nw1LLtEVKpIOP7DsrlxEQjPw1+nrj4/Ilb47Bqpq\r"
            };

        public static string[] GetEcdsaIntermediateCATestCert() => new string[]
            {
                "\rMIICRTCCAeugAwIBAgICEAAwCgYIKoZIzj0EAwIwYTELMAkGA1UEBhMCVVMxCzAJ\rBgNVBAgMAldBMQswCQYDVQQHDAJzczELMAkGA1UECgwCc3MxCzAJBgNVBAsMAnNz\rMQswCQYDVQQDDAJzczERMA8GCSqGSIb3DQEJARYCc3MwHhcNMTUwNTA1MDAwMDAw\rWhcNMjQxMjMxMjM1OTU5WjBUMQswCQYDVQQDDAJzczELMAkGA1UECAwCV0ExCzAJ\rBgNVBAYTAlVTMREwDwYJKoZIhvcNAQkBFgJzczELMAkGA1UECgwCc3MxCzAJBgNV\rBAsMAnNzMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1Di5/tzZFOG1KrfoPwBa\rfgjF9I",
                "DWI7EL5DIeowGfr/MyUmtwULyrLE2bAQUGv9KdH2oPg6aK//WutYqli6MN\rXaOBnzCBnDAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBSN7L0ZvMw8kjXF4iQm\rqp+qYYRbTzAfBgNVHSMEGDAWgBTNmen1wYUOUouvXOzNt+4Gk2ox1zALBgNVHQ8E\rBAMCAaYwEwYDVR0lBAwwCgYIKwYBBQUHAwEwJwYDVR0RBCAwHoINYS5leGFtcGxl\rLmNvbYINYi5leGFtcGxlLmNvbTAKBggqhkjOPQQDAgNIADBFAiBETS1txVUaZl8E\rWagr5+OFGbHEluKTVD3hltzIjnJ+eAIhAIJbxmhIItZyEYpK6Pwy8eIWWO0u9Eu9\rg4oUYwl08mbk\r"
            };

        public static string GetEcdsaTestEdgeHubSignature() => "SQG8DZxbNkLqpcBeaa1EgfPcukqHkmWLahbNwv6xLxEyXQle8/gOFnzbR6GBzan++fMe2EI9l0b0QWtFbxAZCA==";

        public static string[] GetRsaSignerTestCert() => new string[]
            {
                "\rMIIFxTCCA62gAwIBAgICEAAwDQYJKoZIhvcNAQELBQAwVDELMAkGA1UEAwwCc3Mx\rCzAJBgNVBAgMAldBMQswCQYDVQQGEwJVUzERMA8GCSqGSIb3DQEJARYCc3MxCzAJ\rBgNVBAoMAnNzMQswCQYDVQQLDAJzczAeFw0xNTA1MDUwMDAwMDBaFw0yNDEyMzEy\rMzU5NTlaMFQxCzAJBgNVBAMMAnNzMQswCQYDVQQIDAJXQTELMAkGA1UEBhMCVVMx\rETAPBgkqhkiG9w0BCQEWAnNzMQswCQYDVQQKDAJzczELMAkGA1UECwwCc3MwggIi\rMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQC/kWLjEMiQnAc7c3JP8PEIEA58\rmIkdQwaRNWhmZW0OsifBHJJvFgyA5CGEazQaFhcrnMjF2T8/eCoIg7/9+6bsX322\rYLLphXW87aohvUYF0VCcomQRuaYHSxKEGXyAoxmvoywuQP3CELnb6PKM71jLYZn4\r+6kvDHcfZ9vY+jlH4sZRc36sauBwxf3voqt4/07PcHKy6WiPElOd+jZsf82lDTp2\rSLad/cZI8fxJqJth7t9g2b99vEUOtSbs9OliAIwTVAHMIWjsP/dbvNe39TlkrRf3\r7XIWD0RoS6apvE/CFfr4gHFJBxYB553y7KOpdURocnTTQNoMmAqm7bZpVUril48/\r7HBx4qaMz+/h7Vbdn+xhIJmHwGAaylzB9p5lpHdAQ/aSYSSwqkqKh0+hCD8wA5Zt\rcpoSBS+rxGgNtWJrAEjMuatIIu055ckf8lqyD7I8AVSUVuZ5IzumVwgMdRdN3L86\rM9JtknGnGIwFeb4l3S/NCxzhTZmgSY1aZ6uXiAJrvjx5i5J8gx8Mw5OCUGsKks/v\redMV3JFUJiJoleDxu7RQMF5Dy2XlKe26/QiM4DdRyCv7GvDO6oMv9Gudl7FEnt/a\roxibOWppmgEI5fHyPwZdiMPpu7qL",
                "+6AbkFAOQjyTh3Ri5Om9YaXV94zeTrL36ZsR\rA/s29xO/pB437azkcwIDAQABo4GgMIGdMAwGA1UdEwEB/wQCMAAwHQYDVR0OBBYE\rFHoCPM2gglKW8RF8+O/ao3wZMlZsMB8GA1UdIwQYMBaAFLVFJPuERtKpzqIC6fLQ\rZncCpGFaMAsGA1UdDwQEAwIBpjATBgNVHSUEDDAKBggrBgEFBQcDATArBgNVHREE\rJDAigg9hLmEuZXhhbXBsZS5jb22CD2IuYi5leGFtcGxlLmNvbTANBgkqhkiG9w0B\rAQsFAAOCAgEAEiLZpeycdnGWwNUS8LqcLVuJgAJEe10XgKWeUVHBSx3z1thzliXP\rbVsxiiW9V+3lNSaJJSbQkJVPDLV/sKdmSp3dQ/ll3abeSnLzap5FCco1wm2Ru8Vb\rNdJrRLW2hyQ+TFUrWGr9PqK5q6qKuQUywidFZkSvpLOL1eW5jTqUli1GzZio7YD1\rF/Qd4RBbKQTHbtrUMLwJujKIkAh/9dG13WevtdysxaLOYCmckytbE5Af+m1SSERa\r9FjUu22FAwIm9hk64NQgDlML6JKBj03rts51q+FO+D6U6c/VR3rmtZpvqy/Okf4X\rO82+SiTQ1EHLQpIKJhdAfJ7tpDMu0Hz3+qjRX1B8gpK4rnUoPMmmy4cTGjbDzKlL\rcJIP66xbR4tM0O8v1eWu4fJgHlPuYjok/tiIAxZFs8SKomeIEJSNaiOawp5XOshR\r3dggXZey6TDBB4uO2jgGNBQwu4vrFYlZVCcivPbKutjNHB3uhiBrA0yyeD/df1yX\rqwzlSg7cay0WMAbddK0jCFmrXbyRyAuoP/HB1UdQ7LygjsvPdf626xd9w6PihgxD\r9i0AeEqTVdwWfPpiDtRxGhJv/Kz9k17dVFYnJG7oMJrmZJ4kCg+QV/Yy8qRsjiV9\rmPsJeWqhiY5jfO3mC1sEmhb3dzhEW7ntj+xIgCcrlXoU9vkyA/HuPFI=\r"
            };

        public static string[] GetRsaIntermediateCATestCert() => new string[]
            {
                "\rMIIF0TCCA7mgAwIBAgICEAAwDQYJKoZIhvcNAQELBQAwYTELMAkGA1UEBhMCVVMx\rCzAJBgNVBAgMAldBMQswCQYDVQQHDAJzczELMAkGA1UECgwCc3MxCzAJBgNVBAsM\rAnNzMQswCQYDVQQDDAJzczERMA8GCSqGSIb3DQEJARYCc3MwHhcNMTUwNTA1MDAw\rMDAwWhcNMjQxMjMxMjM1OTU5WjBUMQswCQYDVQQDDAJzczELMAkGA1UECAwCV0Ex\rCzAJBgNVBAYTAlVTMREwDwYJKoZIhvcNAQkBFgJzczELMAkGA1UECgwCc3MxCzAJ\rBgNVBAsMAnNzMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA4SGilBtw\rS9iiCka47TzXHEXZuHPKOvZhs+kepLoL/xWWOo/eOcTfnPnD4M0wR0/+Dm3S6zld\rgdojYQPWU48K6mAlNeaTDF16SiTmuMvUvN0c0aifF+YL+9mZuQY30VMXHrjNTQ/e\rTtjEnrp8aOw08eBxFe5zYA+H9a6SnOXuCczH/X9KXlzcYivxPEDc3ImCC7v0DFsB\r7guWp6ZJpU75I5g6hP/tSn/JxaGA1Rp1quZzM0S1Y4eHxqfuhi6mmMFE2TRBRdBn\r5NFtaAmPSqUS51Ie4ryvUlHWCl4jUjKfbjlaFZexPrKAIa293UiJ5J/oywci81Gq\rboAiafB6gCkXFLARy/5aRV+9NGx/+bHPjGn8Q0/1Izhf+qhw8T494IQDkXcSWHNf\r5hw3EFJyrKFsnyMvhcyjNmXaN2JWbnmjw0v4M0xYUjqSyrHAUZVvc3bFQ/5lR2NJ\rbUuopq2f75TT07jvY0LLd5juqjOOntwuVhFpBEow4iT2ELalTI78RiDEcwdyfp9A\rr9Lom94V8yf1ZdJJg4WL4/1uZFOV4dPKxYtd73AISIXusYaI1bdXmPRbJmCZEdN3\rld3bKeuZisdeQmBNl55bnj0IcLQaESbfq77P",
                "PSppZfU8dy8n4gznMtH9jBqRhU4L\r0hkbeMp6DcLbcC9NBqUImNz9jnCFlxzUEeECAwEAAaOBnzCBnDAPBgNVHRMBAf8E\rBTADAQH/MB0GA1UdDgQWBBS1RST7hEbSqc6iAuny0GZ3AqRhWjAfBgNVHSMEGDAW\rgBQusZAXF6xOrlU/BDkAJLwipDfQszALBgNVHQ8EBAMCAaYwEwYDVR0lBAwwCgYI\rKwYBBQUHAwEwJwYDVR0RBCAwHoINYS5leGFtcGxlLmNvbYINYi5leGFtcGxlLmNv\rbTANBgkqhkiG9w0BAQsFAAOCAgEADhMBwTaOemrA2YfYAz6G5VMKVqoi2buZbaUM\rCWBE4Laj0fjGmBMiDoch5jn4yhvVoLrCf1cWJC/CH2XZqBuxxaayQyeLNJ/z311b\rlrjosRURhmEDgE1SRKfHcN9GdywAsjvmCQkB5j5toBKSzLrRTEoP0fXhaicppHCp\rnUgut2b65Nlj9hYHSkIYujYaFG4vPjJD145yXd+HuwHeMqCunvVm50IsaVoA8OD5\rdg6zPSiecJQFlXIFNGs1kniRmMGOnHMzjM+uUE/uUfrdRiT2e0uq+FPWeYVWlHsO\rRzXYcW5iT23fzp2F6B6tOcACrHt1jMmU7QZVvcAo59aLdSeL+Dbvz8BD8tM5y4mn\rZgH8uIt2VI3uCaj5yVtl58X81//z5w94ihQKpzYZAGklVxCej4npp3g3usQS0ANO\r7bqPN9JVHM5VyxVKyFCSpmwh1cCEoPKJAAm6X/LEgZon6Mq8bBXAiKm06S272umT\rKQ18PjGZWSLJwbhutR2MGCtwbUjIokAWPej6pcmEzxy0wK7xtCMEqiH5hRntHAD8\ry5zTOfJ/XXBb8C56GkDhqjD7lu6gPHZLRYBkRIYHHMQi+xhK9j0k/wRoTiiTwkCw\rctkTDJW4O+im8RrylFeeWTfRsJ7PIDjGp89y+U8aAr5kZEFBT2h6RMDBoSLV9soX\ri4H2KRo=\r"
            };

        public static string GetRsaTestEdgeHubSignature() => "DPzArsdn1Dng+NHY7YsFZsIMXtBN+pf4StLGv1aZ7OrbEcY+w8FcBht525J68M2HOXgMDoa1kTOYsS4zneAyDBJ/8RsEFVNX2d43umjf+W7oB8+UiqGcPwYlLpE8AJz38wBA3tqmhXdjyYJ+VOcHufeMUFPhbzZEUcPdymoP+E4mNkhNayioFLP08eKqAcG7fGi7eIHVTSRktPiMHbhdLChsxRoUhRhc53kcieUYu0vYK23raMLDmoRDEgJNKzvqcYaAgEg1Ami+ZEunK4oQtOUpKGCOsWXKppLP3BqsL+e+rzwNSKoz5dl2VDz+QGPbIa5OFSDXvBaSMQPhxo9O3AnhHsriunKgOq/J3jQ15i9UR74qVGWkUFJ0Th6lIxLeOipSRFXaI/HX+yCIpnd8CAMY/p0fSnukLo1e4fVYwwi5Wwj8yC3Py686bDsBZhcXaElBlCkYTW3Pcis26+ISrhAC5mbjbqQU19Uai/MP9XnHSoNdUDnEwwKS0vwjA4MAjGZDCkaTMdJQORWjdX4p7AiWwyRnTF810E5/aQlMkLnwhKFMQgkh0LLXfQHTQlNlXD7DZHGCrO83XlQzXIyVFGw5hc2M5UKhfw/A6AARVIsbxysbiUglTVMf4d0s0VVuTMNqaqvTytHstq3clR/c0No88WjCxRL/l42inKDHRBo=";
    }
}
