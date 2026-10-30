package my.webgso;

import java.util.*;
import my.webgso.Models.*;

public record PlantConfigFilter(int plant, String type, String result) {
  public PlantConfigFilter {
    if (plant < 0
        || !Set.of("", "DDQ", "MMF").contains(type)
        || !Set.of("", "Success", "Failed", "Not tested").contains(result))
      throw new IllegalArgumentException("Choose a valid plant, API type and test result.");
  }

  public boolean api(int id, String dataset, Map<String, ApiTestHistoryService.Entry> latest) {
    String canonical = dataset.equals("DDQ") ? "Rolling 24 Hour" : dataset;
    if (!type.isEmpty() && !type.equals(canonical.equals("MMF") ? "MMF" : "DDQ")) return false;
    var test = latest.get(ApiTestHistoryService.key(id, canonical));
    return result.isEmpty() || result.equals(test == null ? "Not tested" : test.outcome());
  }

  public boolean matches(
      LssPlant p,
      List<PlantApiConfiguration> links,
      Map<String, ApiTestHistoryService.Entry> latest) {
    if (plant != 0 && plant != p.id) return false;
    if (type.isEmpty() && result.isEmpty()) return true;
    var names = new HashSet<String>();
    if (p.ddqApiEndpoint != null && !p.ddqApiEndpoint.isBlank()) names.add("DDQ");
    if (p.mmfApiEndpoint != null && !p.mmfApiEndpoint.isBlank()) names.add("MMF");
    links.stream()
        .filter(link -> link.plantId == p.id && link.apiLink != null && !link.apiLink.isBlank())
        .forEach(link -> names.add(link.dataType));
    return names.stream().anyMatch(dataset -> api(p.id, dataset, latest));
  }
}
