package my.webgso;

import java.time.LocalDateTime;
import org.springframework.stereotype.Service;

/** A connection test reads and parses the response without importing measurements. */
@Service
public class PlantApiProbe {

  private final PlantApiClient client;
  private final PayloadTranslator translator;

  public PlantApiProbe(PlantApiClient client, PayloadTranslator translator) {
    this.client = client;
    this.translator = translator;
  }

  public int verify(String url, String apiType, String dataType, int plantId) {
    return inspect(url, apiType, dataType, plantId).readings();
  }

  public record ProbeResult(
      PlantApiClient.Response response, int readings, String freshnessWarning) {}

  public ProbeResult inspect(String url, String apiType, String dataType, int plantId) {
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
    try {
      var parsed =
          translator.translate(
              response.payload(), response.contentType(), apiType, dataType, plantId, 0, now, 15);
      if (parsed.readings().isEmpty())
        throw new IllegalArgumentException(
            ApiFailureGuide.noMeasurements(response) + " The URL was not saved.");
      return new ProbeResult(
          response,
          parsed.readings().size(),
          ApiDataFreshness.warning(parsed.readings(), dataType, now));
    } catch (IllegalArgumentException e) {
      throw new PlantApiClient.Failure(e.getMessage(), response);
    }
  }
}
