package my.webgso;

import static org.assertj.core.api.Assertions.*;

import org.junit.jupiter.api.Test;

class ApiFailureGuideTests {
  @Test
  void diagnosticsSeparateTransportHttpAndPayloadFailuresWithoutRemoteSecrets() {
    assertThat(ApiFailureGuide.diagnose(new IllegalArgumentException("wrapper",
        new java.net.SocketTimeoutException("secret"))).reason()).isEqualTo("Request timed out");
    assertThat(ApiFailureGuide.diagnose(new java.net.ConnectException("secret")).reason())
        .isEqualTo("API server unreachable");
    for (int status : new int[]{401, 403, 404, 429, 500, 302}) {
      var diagnosis = ApiFailureGuide.diagnose(new PlantApiClient.Failure("secret",
          new PlantApiClient.Response(status, "text/plain", "secret")));
      assertThat(diagnosis.reason()).contains(status == 302 ? "redirect" : "HTTP " + status);
      assertThat(diagnosis.reason() + diagnosis.action()).doesNotContain("secret");
    }
    assertThat(ApiFailureGuide.diagnose(new PlantApiClient.Failure("bad format",
        new PlantApiClient.Response(200, "text/html", "<html>secret</html>"))).reason())
        .isEqualTo("Response data could not be read");
    assertThat(ApiFailureGuide.diagnose(new PlantApiClient.Failure("waiting",
        new PlantApiClient.Response(200, "application/json", "{\"data_status\":\"awaiting_broker\"}"))).reason())
        .isEqualTo("Waiting for MQTT measurements");
    assertThat(ApiFailureGuide.diagnose(new IllegalArgumentException("Use HTTPS. Internal or HTTP hosts must be explicitly allowed.")).reason())
        .isEqualTo("API host or protocol is not allowed");
  }
  @Test
  void explainsNetworkFailuresWithoutExposingExceptionSecrets() {
    assertThat(ApiFailureGuide.connection(new java.net.SocketTimeoutException("secret")))
        .contains("timed out", "VPN")
        .doesNotContain("secret");
    assertThat(ApiFailureGuide.connection(new RuntimeException(new java.net.ConnectException())))
        .contains("host and port", "service is running");
    assertThat(ApiFailureGuide.connection(new java.net.UnknownHostException()))
        .contains("hostname");
    assertThat(ApiFailureGuide.connection(new javax.net.ssl.SSLException("secret")))
        .contains("HTTPS certificate")
        .doesNotContain("secret");
    assertThat(ApiFailureGuide.connection(new RuntimeException("secret"))).doesNotContain("secret");
  }

  @Test
  void distinguishesWaitingForBrokerFromEmptyOrUnsupportedData() {
    assertThat(
            ApiFailureGuide.noMeasurements(
                new PlantApiClient.Response(
                    200, "application/json", "{\"data_status\":\"awaiting_broker\"}")))
        .contains("HTTP 200", "MQTT broker");
    assertThat(
            ApiFailureGuide.noMeasurements(
                new PlantApiClient.Response(200, "text/plain", "not measurements")))
        .contains("connection succeeded", "data format")
        .doesNotContain("MQTT");
  }
}
