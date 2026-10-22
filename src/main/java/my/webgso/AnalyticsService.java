package my.webgso;

import java.math.BigDecimal;
import java.util.*;
import my.webgso.Models.*;

public final class AnalyticsService {
  public record Grouped(List<LssPlant> plants, List<SolarApiReading> readings) {}

  private AnalyticsService() {}

  public static String region(String state) {
    return switch (state) {
      case "Kedah", "Perlis", "Penang", "Perak" -> "Northern";
      case "Selangor", "Negeri Sembilan" -> "Central";
      case "Johor", "Melaka" -> "Southern";
      case "Pahang", "Terengganu", "Kelantan" -> "East Coast";
      default -> "Not assigned";
    };
  }

  public static Grouped aggregate(
      List<LssPlant> plants, List<SolarApiReading> readings, String scope, boolean mmf) {
    if (!Set.of("State", "Region").contains(scope)) return new Grouped(plants, readings);
    var groupedPlants = new TreeMap<String, List<LssPlant>>();
    for (var p : plants)
      groupedPlants
          .computeIfAbsent(
              scope.equals("State") ? p.state : region(p.state), k -> new ArrayList<>())
          .add(p);
    var outputPlants = new ArrayList<LssPlant>();
    var outputReadings = new ArrayList<SolarApiReading>();
    int id = -1;
    for (var entry : groupedPlants.entrySet()) {
      var location = new LssPlant();
      location.id = id--;
      location.plantId = entry.getKey();
      location.name = entry.getKey();
      location.state = entry.getKey();
      location.capacityMw =
          entry.getValue().stream().map(p -> p.capacityMw).reduce(BigDecimal.ZERO, BigDecimal::add);
      outputPlants.add(location);
      Set<Integer> ids = new HashSet<>();
      entry.getValue().forEach(p -> ids.add(p.id));
      record Key(String parameter, java.time.LocalDateTime time, String unit) {}
      var values = new LinkedHashMap<Key, List<SolarApiReading>>();
      for (var r : readings)
        if (ids.contains(r.plantId))
          values
              .computeIfAbsent(
                  new Key(
                      r.parameter.toUpperCase(Locale.ROOT),
                      r.intervalStart,
                      r.unit.toUpperCase(Locale.ROOT)),
                  k -> new ArrayList<>())
              .add(r);
      for (var group : values.values()) {
        var source = group.getFirst();
        var r = new SolarApiReading();
        r.id = source.id;
        r.plantId = location.id;
        r.dataType = source.dataType;
        r.parameter = source.parameter;
        r.unit = source.unit;
        r.intervalStart = source.intervalStart;
        r.intervalEnd = source.intervalEnd;
        r.intervalMinutes = source.intervalMinutes;
        r.submissionDateTime =
            group.stream()
                .map(v -> v.submissionDateTime)
                .max(java.time.LocalDateTime::compareTo)
                .orElseThrow();
        r.value =
            mmf
                ? Policies.average(group.stream().map(v -> v.value).toList())
                : group.stream().map(v -> v.value).reduce(BigDecimal.ZERO, BigDecimal::add);
        r.isSynthetic = true;
        r.syntheticReason =
            (mmf ? "Location average" : "Location total")
                + " calculated from "
                + group.size()
                + " plant(s)";
        outputReadings.add(r);
      }
    }
    outputReadings.sort(
        Comparator.comparing((SolarApiReading r) -> r.intervalStart).thenComparing(r -> r.plantId));
    return new Grouped(outputPlants, outputReadings);
  }
}
