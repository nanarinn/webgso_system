package my.webgso;

import java.time.LocalDateTime;
import org.springframework.stereotype.Service;

/** A connection test reads and parses the response without importing measurements. */
@Service
public class PlantApiProbe {
  private static final com.fasterxml.jackson.databind.ObjectMapper JSON =
      new com.fasterxml.jackson.databind.ObjectMapper();
  private final PlantApiClient client;
  private final PayloadTranslator translator;

  public PlantApiProbe(PlantApiClient client, PayloadTranslator translator) {
    this.client = client;
    this.translator = translator;
  }

  public int verify(String url, String apiType, String dataType, int plantId) {
    client.validate(url);
    var now = LocalDateTime.now(Policies.LOCAL);
    String resolved =
        url.replace("{date}", now.toLocalDate().toString())
            .replace("{submissionDate}", now.toLocalDate().toString())
            .replace("{interval}", "15");
    var response = client.fetch(resolved);
    if (response == null || response.status() < 200 || response.status() >= 300)
      throw new IllegalArgumentException(
          "The API did not return a successful response. The URL was not saved.");
    var parsed =
        translator.translate(
            response.payload(), response.contentType(), apiType, dataType, plantId, 0, now, 15);
    if (parsed.readings().isEmpty()) {
      try {
        var body = JSON.readTree(response.payload());
        if (body != null && body.path("data_status").asText().equalsIgnoreCase("awaiting_broker"))
          throw new IllegalArgumentException(
              "API server is reachable (HTTP "
                  + response.status()
                  + "), but it reports awaiting_broker and has no readable MMF measurements. Check"
                  + " the MQTT broker and data publisher, then test again. The URL was not saved.");
      } catch (com.fasterxml.jackson.core.JsonProcessingException ignored) {
        // Other response formats keep the normal no-measurements message.
      }
      throw new IllegalArgumentException(
          "The API did not return readable measurements. The URL was not saved.");
    }
    return parsed.readings().size();
  }
}
