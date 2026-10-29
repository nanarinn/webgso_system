package my.webgso;

/** Fixed, actionable explanations; never include remote error bodies or credentials. */
public final class ApiFailureGuide {
  private ApiFailureGuide() {}

  public static String connection(Throwable error) {
    for (Throwable cause = error; cause != null; cause = cause.getCause()) {
      if (cause instanceof java.net.SocketTimeoutException
          || cause instanceof java.util.concurrent.TimeoutException)
        return "API request timed out. The server did not respond in time. Check its availability"
                   + " and network/VPN connection, then try again.";
      if (cause instanceof java.net.UnknownHostException)
        return "API host could not be resolved or is not permitted. Check the hostname and the"
                   + " server's allowed-host configuration.";
      if (cause instanceof java.net.ConnectException)
        return "Could not connect to the API server. Check the host and port, confirm the API"
                   + " service is running, and check network/VPN access.";
      if (cause instanceof javax.net.ssl.SSLException)
        return "The API TLS certificate or secure connection could not be verified. Ask the API"
                   + " provider to check its HTTPS certificate and TLS settings.";
    }
    return "API connection failed. Check network access and the server's allowed-host"
               + " configuration, then try again.";
  }

  public static String noMeasurements(PlantApiClient.Response response) {
    try {
      var body = new com.fasterxml.jackson.databind.ObjectMapper().readTree(response.payload());
      if (body != null && body.path("data_status").asText().equalsIgnoreCase("awaiting_broker"))
        return "API server is reachable (HTTP "
            + response.status()
            + "), but it reports awaiting_broker and has no readable MMF measurements. Check the"
            + " MQTT broker and data publisher, then test again.";
    } catch (com.fasterxml.jackson.core.JsonProcessingException ignored) {
    }
    return "The API did not return readable measurements (HTTP "
        + response.status()
        + "). The connection succeeded, but the response is empty or its data format is"
        + " unsupported. Check the API response and selected DDQ/MMF dataset.";
  }
}
