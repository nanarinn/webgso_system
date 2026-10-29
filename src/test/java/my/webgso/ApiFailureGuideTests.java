package my.webgso;

import static org.assertj.core.api.Assertions.*;

import org.junit.jupiter.api.Test;

class ApiFailureGuideTests {
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
