package my.webgso;

/** Fixed, actionable explanations; never include remote error bodies or credentials. */
public final class ApiFailureGuide {
  private ApiFailureGuide() {}

  public record Diagnosis(String reason, String action) {}

  public static Diagnosis diagnose(Throwable error) {
    for (Throwable cause = error; cause != null; cause = cause.getCause()) {
      if (cause instanceof java.net.SocketTimeoutException
          || cause instanceof java.util.concurrent.TimeoutException)
        return new Diagnosis("Request timed out", "Check whether the API server is running and reachable through your network or VPN, then retry.");
      if (cause instanceof java.net.UnknownHostException)
        return new Diagnosis("Hostname could not be resolved", "Check the API hostname, DNS and the configured allowed hosts.");
      if (cause instanceof java.net.ConnectException)
        return new Diagnosis("API server unreachable", "Check the host, port, API service and firewall or VPN access.");
      if (cause instanceof javax.net.ssl.SSLException)
        return new Diagnosis("HTTPS certificate or TLS error", "Ask the API provider to check the certificate and TLS configuration.");
    }
    if (error instanceof PlantApiClient.Failure failure && failure.response() != null) {
      int status = failure.response().status();
      if (status == 401 || status == 403)
        return new Diagnosis("API access denied (HTTP " + status + ")", "Check API credentials and permissions with the provider.");
      if (status == 404)
        return new Diagnosis("API endpoint not found (HTTP 404)", "Check the URL path and selected dataset with the provider.");
      if (status == 429)
        return new Diagnosis("API rate limit reached (HTTP 429)", "Wait for the provider's retry interval before testing again.");
      if (status >= 500)
        return new Diagnosis("API server error (HTTP " + status + ")", "Ask the provider to check the API service, then retry when it is available.");
      if (status >= 300 && status < 400)
        return new Diagnosis("API returned a redirect", "Use the final API endpoint URL. Redirects are not followed automatically.");
      if (status < 200 || status >= 300)
        return new Diagnosis("API rejected the request (HTTP " + status + ")", "Check the URL and request requirements with the provider.");
      if (noMeasurements(failure.response()).contains("awaiting_broker"))
        return new Diagnosis("Waiting for MQTT measurements", "Check the MQTT broker connection and weather-station publisher.");
      return new Diagnosis("Response data could not be read", "Use View raw response to check for empty data, an HTML page, malformed JSON/XML/CSV or a dataset that does not match DDQ/MMF.");
    }
    String message = java.util.Objects.toString(error.getMessage(), "").toLowerCase(java.util.Locale.ROOT);
    if (message.contains("https") || message.contains("permitted") || message.contains("allowed") || message.contains("private"))
      return new Diagnosis("API host or protocol is not allowed", "Use HTTPS or ask the server administrator to explicitly allow this API host. Keep host restrictions enabled.");
    if (message.contains("url") || message.contains("endpoint") || message.contains("dataset"))
      return new Diagnosis("Check the API URL and dataset", "Enter a complete HTTP/HTTPS API URL and choose the matching DDQ or MMF dataset.");
    return new Diagnosis("API test could not be completed", "Check the message above, API availability and server configuration before retrying.");
  }

  public static java.util.Map<String, Object> details(Throwable error) {
    var diagnosis = diagnose(error);
    return java.util.Map.of("apiTestFailureReason", diagnosis.reason(), "apiTestFailureAction", diagnosis.action());
  }

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
