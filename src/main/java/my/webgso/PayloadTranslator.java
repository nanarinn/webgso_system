package my.webgso;

import com.fasterxml.jackson.databind.*;
import java.io.*;
import java.math.*;
import java.time.*;
import java.time.format.*;
import java.util.*;
import java.util.stream.*;
import javax.xml.XMLConstants;
import javax.xml.parsers.*;
import my.webgso.Models.*;
import org.apache.commons.csv.*;
import org.springframework.stereotype.Component;
import org.w3c.dom.*;
import org.xml.sax.InputSource;

@Component
public class PayloadTranslator {
  public static final class StructureFailure extends IllegalArgumentException {
    StructureFailure(String message) {
      super("API data structure is invalid: " + message);
    }
  }

  private static LocalDateTime shifted(LocalDateTime time, long minutes) {
    return time == null ? null : time.plusMinutes(minutes);
  }

  public record Issue(String code, String field, String value, String reason) {}

  public record Result(List<SolarApiReading> readings, List<Issue> issues, String format) {}

  private record Candidate(
      LocalDateTime time, LocalDateTime end, String parameter, BigDecimal value, String unit) {}

  private final ObjectMapper mapper;

  public PayloadTranslator(ObjectMapper mapper) {
    this.mapper = mapper;
  }

  private static final List<String> TIMES =
      List.of(
          "timestamp",
          "dataTimestamp",
          "forecastTimestamp",
          "datetime",
          "date",
          "time",
          "startTime",
          "intervalStart",
          "intervalStartTime",
          "readingTime",
          "recordedAt");
  private static final List<String> PARAMS =
      List.of(
          "parameter",
          "forecastResultParameter",
          "metric",
          "field",
          "measurement",
          "sensorType",
          "name");
  private static final List<String> VALUES =
      List.of("value", "forecastValue", "measurementValue", "reading");

  static String key(String value) {
    return value.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]", "");
  }

  static String first(Map<String, String> fields, List<String> names) {
    for (String name : names)
      for (var item : fields.entrySet())
        if (key(item.getKey()).equals(key(name))) return item.getValue();
    return null;
  }

  static BigDecimal number(String value) {
    try {
      return new BigDecimal(value.trim());
    } catch (Exception e) {
      return null;
    }
  }

  public static LocalDateTime timestamp(String value) {
    if (value == null || value.isBlank()) return null;
    try {
      return OffsetDateTime.parse(value.trim()).atZoneSameInstant(Policies.LOCAL).toLocalDateTime();
    } catch (Exception ignored) {
    }
    try {
      return LocalDateTime.parse(value.trim().replace(' ', 'T'));
    } catch (Exception ignored) {
    }
    for (String pattern : List.of("d/M/uuuu H:mm:ss", "d/M/uuuu H:mm", "uuuu-MM-dd HH:mm:ss"))
      try {
        return LocalDateTime.parse(
            value.trim(),
            DateTimeFormatter.ofPattern(pattern).withResolverStyle(ResolverStyle.STRICT));
      } catch (Exception ignored) {
      }
    for (String pattern : List.of("uuuu-MM-dd", "d/M/uuuu"))
      try {
        return LocalDate.parse(
                value.trim(),
                DateTimeFormatter.ofPattern(pattern).withResolverStyle(ResolverStyle.STRICT))
            .atStartOfDay();
      } catch (Exception ignored) {
      }
    return null;
  }

  static boolean metadata(String name) {
    String k = key(name);
    return TIMES.stream().anyMatch(t -> key(t).equals(k))
        || Set.of(
                "year",
                "month",
                "day",
                "period",
                "unit",
                "parameter",
                "name",
                "submissiondatetime",
                "submissiondate",
                "submissiontimestamp",
                "submittedat",
                "endtime",
                "intervalend",
                "intervalendtime")
            .contains(k)
        || k.endsWith("id")
        || Stream.of("code", "status", "error", "count", "interval", "latitude", "longitude")
            .anyMatch(k::contains);
  }

  private void fields(
      Map<String, String> fields, LocalDateTime inherited, int interval, List<Candidate> result) {
    String suppliedTime = first(fields, TIMES);
    LocalDateTime time = timestamp(suppliedTime);
    if (suppliedTime != null && time == null)
      throw new StructureFailure(
          "A timestamp is missing or has an invalid date/time format. Provide a valid timestamp for"
              + " every measurement.");
    boolean composite = first(fields, List.of("year", "month", "day", "period")) != null;
    if (time == null)
      try {
        time =
            LocalDate.of(
                    Integer.parseInt(first(fields, List.of("year"))),
                    Integer.parseInt(first(fields, List.of("month"))),
                    Integer.parseInt(first(fields, List.of("day"))))
                .atStartOfDay()
                .plusMinutes((Integer.parseInt(first(fields, List.of("period"))) - 1L) * interval);
      } catch (Exception ignored) {
      }
    if (composite && time == null)
      throw new StructureFailure(
          "Year, month, day and period must form a valid forecast date and interval.");
    if (composite
        && number(first(fields, List.of("period"))) != null
        && number(first(fields, List.of("period"))).signum() <= 0)
      throw new StructureFailure("Forecast period must be a positive whole number.");
    if (time == null) time = inherited;
    LocalDateTime end =
        timestamp(first(fields, List.of("endTime", "intervalEnd", "intervalEndTime")));
    String parameter = first(fields, PARAMS),
        unit = Objects.toString(first(fields, List.of("unit", "valueUnit", "measurementUnit")), "");
    BigDecimal value = number(first(fields, VALUES));
    String suppliedValue = first(fields, VALUES);
    if (suppliedValue != null && value == null)
      throw new StructureFailure(
          "A measurement value is missing or is not numeric. Check value, forecastValue or reading"
              + " fields.");
    if (parameter != null && parameter.isBlank())
      throw new StructureFailure(
          "A measurement parameter is blank. Provide its field or sensor name.");
    String suppliedEnd = first(fields, List.of("endTime", "intervalEnd", "intervalEndTime"));
    if (suppliedEnd != null && end == null)
      throw new StructureFailure("An interval end has an invalid date/time format.");
    if (end != null && time != null && !end.isAfter(time))
      throw new StructureFailure("Interval end must be after interval start.");
    for (var item : fields.entrySet()) {
      String name = key(item.getKey());
      boolean measurement =
          name.matches("(?:mmf|sensor|value|reading)[1-9][0-9]*")
              || Set.of(
                      "power",
                      "forecastvalue",
                      "ambienttemp",
                      "humidity",
                      "windspeed",
                      "irradiance")
                  .contains(name)
              || Policies.MMF_UNITS.keySet().stream().anyMatch(p -> key(p).equals(name));
      // Some live MMF feeds return the literal string "NaN" for an unavailable sensor while
      // other sensors in the same payload remain valid. Ignore only that unavailable value so a
      // single sensor cannot prevent every valid reading from being displayed.
      if (measurement && number(item.getValue()) == null) continue;
    }
    if (first(
                fields,
                List.of(
                    "parameter", "forecastResultParameter", "metric", "measurement", "sensorType"))
            != null
        && suppliedValue == null
        && fields.keySet().stream()
            .noneMatch(k -> key(k).matches("(?:mmf|sensor|value|reading)[1-9][0-9]*")))
      throw new StructureFailure(
          "A measurement parameter has no numeric value or sensor readings.");
    if (parameter != null && value != null) {
      result.add(new Candidate(time, end, parameter, value, unit));
      return;
    }
    boolean sensors = false;
    if (parameter != null)
      for (var item : fields.entrySet())
        if (key(item.getKey()).matches("(?:mmf|sensor|value|reading)[1-9][0-9]*")) {
          var sensor = number(item.getValue());
          if (sensor != null) {
            result.add(new Candidate(time, end, parameter, sensor, unit));
            sensors = true;
          }
        }
    if (sensors) return;
    for (var item : fields.entrySet())
      if (!metadata(item.getKey()) && timestamp(item.getKey()) == null) {
        var n = number(item.getValue());
        if (n != null) result.add(new Candidate(time, end, item.getKey(), n, unit));
      }
  }

  private void json(
      JsonNode node,
      LocalDateTime inherited,
      String parameter,
      int interval,
      List<Candidate> result) {
    if (node.isArray()) {
      boolean numeric = parameter != null && !metadata(parameter) && !node.isEmpty();
      for (var child : node) numeric &= child.isValueNode() && number(child.asText()) != null;
      if (numeric) {
        int i = 0;
        for (var child : node)
          result.add(
              new Candidate(
                  shifted(inherited, (long) i++ * interval),
                  null,
                  parameter,
                  number(child.asText()),
                  ""));
      } else for (var child : node) json(child, inherited, parameter, interval, result);
      return;
    }
    if (!node.isObject()) return;
    Map<String, String> scalars = new LinkedHashMap<>();
    node.fields()
        .forEachRemaining(
            e -> {
              if (e.getValue().isValueNode()) scalars.put(e.getKey(), e.getValue().asText());
            });
    LocalDateTime explicit = timestamp(first(scalars, TIMES));
    if (explicit != null) inherited = explicit;
    JsonNode headers = node.has("columns") ? node.get("columns") : node.get("headers"),
        rows = node.has("rows") ? node.get("rows") : node.get("data");
    if (headers != null
        && headers.isArray()
        && rows != null
        && rows.isArray()
        && !rows.isEmpty()
        && rows.get(0).isArray()) {
      int i = 0;
      for (var row : rows) {
        Map<String, String> f = new LinkedHashMap<>();
        for (int j = 0; j < headers.size(); j++)
          f.put(headers.get(j).asText(), j < row.size() ? row.get(j).asText() : "");
        fields(f, shifted(inherited, (long) i++ * interval), interval, result);
      }
      return;
    }
    var arrays = new LinkedHashMap<String, JsonNode>();
    node.fields()
        .forEachRemaining(
            e -> {
              if (e.getValue().isArray()) arrays.put(e.getKey(), e.getValue());
            });
    boolean columnar =
        arrays.keySet().stream()
            .anyMatch(
                k ->
                    TIMES.stream().anyMatch(t -> key(t).equals(key(k))) || key(k).equals("period"));
    if (columnar) {
      int size = arrays.values().stream().mapToInt(JsonNode::size).max().orElse(0);
      for (int i = 0; i < size; i++) {
        var f = new LinkedHashMap<>(scalars);
        for (var e : arrays.entrySet())
          if (i < e.getValue().size()) f.put(e.getKey(), e.getValue().get(i).asText());
        fields(f, shifted(inherited, (long) i * interval), interval, result);
      }
      return;
    }
    fields(scalars, inherited, interval, result);
    var iter = node.fields();
    while (iter.hasNext()) {
      var child = iter.next();
      LocalDateTime keyed = timestamp(child.getKey());
      if (child.getValue().isContainerNode())
        json(child.getValue(), keyed != null ? keyed : inherited, child.getKey(), interval, result);
      else if (keyed != null && number(child.getValue().asText()) != null)
        result.add(
            new Candidate(
                keyed,
                null,
                parameter == null ? "Value" : parameter,
                number(child.getValue().asText()),
                ""));
    }
  }

  /**
   * Some live MMF endpoints return one object per physical sensor without a timestamp, for
   * example {"MMF1":{"amb_temp":31,"ws":2.1},"MMF2":{...}}. Preserve the sensor name in
   * the displayed parameter and use the trusted server-side receipt/submission time.
   */
  private boolean mmfSnapshot(
      JsonNode root, LocalDateTime receivedAt, List<Candidate> result, List<Issue> issues) {
    if (!root.isObject()) return false;
    boolean matched = false;
    var sensors = root.fields();
    while (sensors.hasNext()) {
      var sensor = sensors.next();
      if (!sensor.getKey().matches("(?i)MMF[1-9][0-9]*") || !sensor.getValue().isObject())
        continue;
      matched = true;
      var measurements = sensor.getValue().fields();
      while (measurements.hasNext()) {
        var measurement = measurements.next();
        if (!measurement.getValue().isValueNode()) continue;
        BigDecimal value = number(measurement.getValue().asText());
        if (value == null) continue;
        String raw = key(measurement.getKey());
        String label =
            switch (raw) {
              case "ambtemp" -> "Ambient Temperature";
              case "ws" -> "Wind Speed";
              case "wd" -> "Wind Direction";
              case "humid" -> "Humidity";
              case "precip" -> "Precipitation";
              case "barom" -> "Barometric Pressure";
              case "atm" -> "Atmospheric Pressure";
              case "pvtemp" -> "PV Temperature";
              case "mwa" -> "MW per Area";
              case "tgi" -> "TGI";
              case "tghi" -> "TGHI";
              case "poa" -> "POA Irradiance";
              default -> measurement.getKey();
            };
        String unit =
            switch (raw) {
              case "ambtemp", "pvtemp" -> "C";
              case "ws" -> "m/s";
              case "wd" -> "°";
              case "humid" -> "%";
              case "precip" -> "mm/h";
              case "barom" -> "hPa";
              case "atm" -> "atm";
              case "mwa" -> "MW";
              case "tgi", "tghi", "poa" -> "W/m2";
              default -> "";
            };
        result.add(
            new Candidate(
                receivedAt,
                null,
                sensor.getKey().toUpperCase(Locale.ROOT) + " · " + label,
                value,
                unit));
      }
    }
    if (matched)
      issues.add(
          new Issue(
              "MMF-002",
              "Timestamp",
              receivedAt.toString(),
              "The API supplied no measurement timestamp; the server receipt time was used."));
    return matched;
  }

  private void xml(Element root, LocalDateTime inherited, int interval, List<Candidate> result) {
    var scalars = new LinkedHashMap<String, String>();
    var nested = new ArrayList<Element>();
    var children = root.getChildNodes();
    for (int i = 0; i < children.getLength(); i++)
      if (children.item(i) instanceof Element element) {
        boolean leaf = true;
        for (int j = 0; j < element.getChildNodes().getLength(); j++)
          if (element.getChildNodes().item(j) instanceof Element) leaf = false;
        if (leaf)
          scalars.put(
              element.getLocalName() == null ? element.getNodeName() : element.getLocalName(),
              element.getTextContent());
        else nested.add(element);
      }
    LocalDateTime time = timestamp(first(scalars, TIMES));
    if (time == null) time = inherited;
    if (nested.isEmpty()) fields(scalars, time, interval, result);
    for (var child : nested) xml(child, time, interval, result);
  }

  public Result translate(
      String payload,
      String contentType,
      String apiType,
      String dataType,
      int plantId,
      long jobId,
      LocalDateTime submission,
      int interval) {
    List<Candidate> candidates = new ArrayList<>();
    var issues = new ArrayList<Issue>();
    String text = payload.strip().replaceFirst("^\\uFEFF", ""), format;
    if (text.toLowerCase(Locale.ROOT).startsWith("<!doctype html")
        || text.toLowerCase(Locale.ROOT).startsWith("<html")
        || contentType.toLowerCase(Locale.ROOT).contains("text/html"))
      throw new IllegalArgumentException("The endpoint returned HTML instead of API data.");
    try {
      if (text.startsWith("{") || text.startsWith("[")) {
        format = "JSON";
        var root = mapper.readTree(text);
        String declared = root.path("data_type").asText("").toUpperCase(Locale.ROOT);
        if (Set.of("DDQ", "MMF").contains(declared) && !declared.equals(apiType))
          throw new StructureFailure(
              "The response declares "
                  + declared
                  + " data, but the selected API type is "
                  + apiType
                  + ". Check the URL and dataset.");
        if (!apiType.equals("MMF") || !mmfSnapshot(root, submission, candidates, issues))
          json(root, null, null, interval, candidates);
      } else if (text.startsWith("<")) {
        format = "XML";
        var factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
        factory.setFeature("http://xml.org/sax/features/external-general-entities", false);
        factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false);
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_DTD, "");
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_SCHEMA, "");
        xml(
            factory
                .newDocumentBuilder()
                .parse(new InputSource(new StringReader(text)))
                .getDocumentElement(),
            null,
            interval,
            candidates);
      } else {
        format = "CSV";
        String first = text.lines().findFirst().orElse("");
        char delimiter =
            Stream.of(',', ';', '\t', '|')
                .max(Comparator.comparingLong(c -> first.chars().filter(v -> v == c).count()))
                .orElse(',');
        try (var parser =
            CSVFormat.DEFAULT
                .builder()
                .setDelimiter(delimiter)
                .setHeader()
                .setSkipHeaderRecord(true)
                .get()
                .parse(new StringReader(text))) {
          for (var row : parser) fields(row.toMap(), null, interval, candidates);
        }
      }
    } catch (StructureFailure e) {
      throw e;
    } catch (Exception e) {
      throw new IllegalArgumentException(
          "Invalid API payload. Expected readable JSON, XML or CSV measurement data. Check response"
              + " syntax and field layout.",
          e);
    }
    long missingTimes = candidates.stream().filter(c -> c.time == null).count();
    if (missingTimes > 0) {
      if (!apiType.equals("MMF"))
        throw new StructureFailure(
            "A measurement is missing its timestamp. Supply timestamp/date-time, a dated parent"
                + " object, or year/month/day/period fields.");
      candidates.removeIf(c -> c.time == null);
      issues.add(
          new Issue(
              "MMF-001",
              "Timestamp",
              Long.toString(missingTimes),
              "MMF measurements without a valid timestamp were ignored."));
    }
    if (candidates.size() > 250_000)
      throw new IllegalArgumentException("The payload contains too many readings.");
    var times = candidates.stream().map(Candidate::time).distinct().sorted().toList();
    var frequencies = new TreeMap<Integer, Integer>();
    for (int i = 1; i < times.size(); i++) {
      int minutes = (int) Duration.between(times.get(i - 1), times.get(i)).toMinutes();
      if (minutes > 0 && minutes <= 1440) frequencies.merge(minutes, 1, Integer::sum);
    }
    int sourceInterval =
        frequencies.entrySet().stream()
            .sorted(
                Map.Entry.<Integer, Integer>comparingByValue()
                    .reversed()
                    .thenComparing(Map.Entry.comparingByKey()))
            .map(Map.Entry::getKey)
            .findFirst()
            .orElse(Math.max(1, interval));
    record Group(LocalDateTime time, String parameter, String unit) {}
    var grouped = new LinkedHashMap<Group, List<Candidate>>();
    for (var c : candidates) {
      int minute = c.time.getHour() * 60 + c.time.getMinute();
      var aligned =
          c.time.toLocalDate().atStartOfDay().plusMinutes(minute - minute % sourceInterval);
      grouped
          .computeIfAbsent(
              new Group(aligned, key(c.parameter), c.unit.toUpperCase(Locale.ROOT)),
              ignored -> new ArrayList<>())
          .add(c);
    }
    var readings = new ArrayList<SolarApiReading>();
    for (var entry : grouped.entrySet()) {
      var group = entry.getValue();
      var first = group.getFirst();
      var reading = new SolarApiReading();
      reading.plantId = plantId;
      reading.collectionJobId = jobId;
      reading.dataType = apiType.equals("MMF") ? "MMF" : Policies.canonical(dataType);
      reading.submissionDateTime = submission;
      reading.intervalStart = entry.getKey().time;
      reading.intervalEnd =
          first.end == null ? reading.intervalStart.plusMinutes(sourceInterval) : first.end;
      reading.intervalMinutes = sourceInterval;
      reading.parameter =
          Policies.MMF_UNITS.keySet().stream()
              .filter(p -> key(p).equals(key(first.parameter)))
              .findFirst()
              .orElse(first.parameter);
      reading.unit =
          first.unit.isBlank()
              ? Policies.MMF_UNITS.getOrDefault(reading.parameter, "")
              : first.unit;
      reading.value = Policies.average(group.stream().map(Candidate::value).toList());
      reading.errorCode = 0;
      reading.errorMessage = "OK";
      if (apiType.equals("MMF"))
        reading.syntheticReason =
            "Stored average of "
                + group.size()
                + " MMF sensor reading(s) exactly as received from the API.";
      else if (group.size() > 1)
        issues.add(
            new Issue(
                "DDQ-002",
                reading.parameter,
                "" + group.size(),
                "Duplicate readings for the same plant, parameter and timestamp."));
      readings.add(reading);
    }
    readings.sort(
        Comparator.comparing((SolarApiReading r) -> r.intervalStart)
            .thenComparing(r -> r.parameter));
    if (readings.isEmpty())
      issues.add(
          new Issue(
              apiType + "-001", "API payload", "0", "No numeric measurement fields were found."));
    if (!apiType.equals("MMF"))
      for (int i = 1; i < times.size(); i++) {
        long missing =
            Duration.between(times.get(i - 1), times.get(i)).toMinutes() / sourceInterval - 1;
        if (missing > 0)
          issues.add(
              new Issue(
                  "DDQ-009",
                  "Forecast timestamps",
                  "" + missing,
                  "Missing timestamps inside supplied data range."));
      }
    return new Result(readings, issues, format);
  }
}
