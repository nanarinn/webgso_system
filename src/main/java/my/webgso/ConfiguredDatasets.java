package my.webgso;

import java.util.*;
import my.webgso.Models.*;

/** Dataset availability comes from saved URLs, never from the complete dataset catalogue. */
public final class ConfiguredDatasets {
  public static final List<String> ALL =
      java.util.stream.Stream.concat(Policies.DDQ_TYPES.stream(), java.util.stream.Stream.of("MMF"))
          .toList();

  private ConfiguredDatasets() {}

  public static List<String> forPlant(LssPlant plant, List<PlantApiConfiguration> links) {
    var available = new HashSet<String>();
    if (plant.ddqApiEndpoint != null && !plant.ddqApiEndpoint.isBlank())
      available.addAll(Policies.DDQ_TYPES);
    if (plant.mmfApiEndpoint != null && !plant.mmfApiEndpoint.isBlank()) available.add("MMF");
    for (var link : links) {
      if (link.plantId != plant.id || link.apiLink == null || link.apiLink.isBlank()) continue;
      if (!ALL.contains(link.dataType)) continue;
      if (Objects.equals(link.apiType, link.dataType.equals("MMF") ? "MMF" : "DDQ"))
        available.add(link.dataType);
    }
    return ALL.stream().filter(available::contains).toList();
  }
}
