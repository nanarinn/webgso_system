package my.webgso;

import java.util.*;
import my.webgso.Models.LssPlant;

/** Exactly one location dimension is applied, including for manually constructed requests. */
public record PlantLocationFilter(String mode, List<String> values) {
  public PlantLocationFilter {
    if (!Set.of("all", "plant", "state", "region", "zone").contains(mode))
      throw new IllegalArgumentException("Choose a valid location filter.");
    values = values.stream().filter(v -> v != null && !v.isBlank()).distinct().toList();
    if (values.size() > 500 || values.stream().anyMatch(v -> v.length() > 100))
      throw new IllegalArgumentException("Too many location selections.");
  }

  public boolean matches(LssPlant plant) {
    if (mode.equals("all") || values.isEmpty()) return true;
    String value =
        switch (mode) {
          case "plant" -> String.valueOf(plant.id);
          case "state" -> plant.state;
          case "region" -> region(plant.state);
          case "zone" -> plant.zone;
          default -> "";
        };
    return values.stream().anyMatch(v -> v.equalsIgnoreCase(value));
  }

  public static String region(String state) {
    // Match legacy uppercase state values to the application's existing region classification.
    String canonical =
        Arrays.stream(Objects.toString(state, "").trim().toLowerCase(Locale.ROOT).split("\\s+"))
            .filter(s -> !s.isBlank())
            .map(s -> Character.toUpperCase(s.charAt(0)) + s.substring(1))
            .reduce((a, b) -> a + " " + b)
            .orElse("");
    return AnalyticsService.region(canonical);
  }
}
