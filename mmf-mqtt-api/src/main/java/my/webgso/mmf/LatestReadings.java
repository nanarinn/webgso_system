package my.webgso.mmf;

import com.fasterxml.jackson.databind.*;
import java.math.BigDecimal;
import java.time.*;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

/** Latest source measurement per station and parameter; never substitutes receipt time. */
public final class LatestReadings {
  private static final ObjectMapper JSON = new ObjectMapper();
  private final Map<String, Reading> latest = new ConcurrentHashMap<>();
  private final String root;
  public record Reading(String station, String parameter, BigDecimal value, String unit,
      String timestamp, Instant instant, String sourceTopic) {}
  public LatestReadings(String root) { this.root = root; }

  public int accept(String topic, byte[] payload, Instant now) throws Exception {
    if (payload.length > 262144 || !topic.startsWith(root + "/")) return 0;
    String[] path = topic.substring(root.length() + 1).split("/", -1);
    if (path.length < 1 || path.length > 2 ||
        !path[0].matches("Weather_Station_SAE(?:_[1-9][0-9]*)?")) return 0;
    JsonNode node = JSON.readTree(payload);
    if (node == null || !node.isObject()) return 0;
    if (path.length == 2) return put(path[0], path[1], node, topic, now);
    int count = 0;
    for (var fields = node.fields(); fields.hasNext();) {
      var entry = fields.next();
      count += put(path[0], entry.getKey(), entry.getValue(), topic, now);
    }
    return count;
  }

  private int put(String station, String source, JsonNode node, String topic, Instant now) {
    String parameter = parameter(source);
    if (parameter == null || !node.isObject() || !node.path("value").isNumber()) return 0;
    OffsetDateTime time;
    try { time = OffsetDateTime.parse(node.path("timestamp").asText()); }
    catch (RuntimeException error) { return 0; }
    if (time.toInstant().isAfter(now.plusSeconds(60))) return 0;
    String unit = node.path("unit").asText("");
    if (unit.length() > 32) return 0;
    Reading reading = new Reading(station, parameter, node.path("value").decimalValue(),
        unit, time.toString(), time.toInstant(), topic);
    String key = station + "/" + parameter;
    boolean[] changed = {false};
    latest.compute(key, (ignored, previous) -> {
      if (previous == null || reading.instant().isAfter(previous.instant())) {
        changed[0] = true; return reading;
      }
      return previous;
    });
    return changed[0] ? 1 : 0;
  }

  static String parameter(String source) {
    return switch (source.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]", "")) {
      case "ambienttemperature", "ambienttemp" -> "AmbientTemp";
      case "globalhorizontalirradiationghi", "globalhorizontolirradiationghi", "ghi", "horirradiance" -> "HorIrradiance";
      case "tgi", "totradiation" -> "TotRadiation";
      case "inclineirradiationpoa", "poa", "incirradiance" -> "IncIrradiance";
      case "windspeedwsp", "windspeed" -> "WindSpeed";
      case "winddirectionwd", "winddir" -> "WindDir";
      case "humidity" -> "Humidity";
      case "precipitation" -> "Precipitation";
      case "barometricpressure", "airpressure" -> "AirPressure";
      case "pvmoduletemperature", "pvtemperature" -> "PVTemperature";
      default -> null;
    };
  }

  public List<Map<String, Object>> snapshot(String station, Instant now) {
    return latest.values().stream().filter(r -> station == null || r.station().equals(station))
        .sorted(Comparator.comparing(Reading::station).thenComparing(Reading::parameter))
        .map(r -> {
          Map<String, Object> row = new LinkedHashMap<>();
          row.put("station", r.station()); row.put("parameter", r.parameter());
          row.put("value", r.value()); row.put("unit", r.unit());
          row.put("timestamp", r.timestamp()); row.put("sourceTopic", r.sourceTopic());
          long age = Math.max(0, Duration.between(r.instant(), now).getSeconds());
          row.put("ageSeconds", age); row.put("status", age <= 300 ? "Fresh" : "Stale");
          return row;
        }).toList();
  }
}
