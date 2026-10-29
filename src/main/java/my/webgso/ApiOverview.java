package my.webgso;

import java.util.*;
import my.webgso.Models.*;

/** One current result for each dataset that still has a configured URL. */
public final class ApiOverview {
  private ApiOverview() {}

  public static List<Map<String, Object>> rows(
      List<LssPlant> plants,
      List<PlantApiConfiguration> links,
      Map<String, ApiTestHistoryService.Entry> latest) {
    var rows = new ArrayList<Map<String, Object>>();
    for (var plant : plants) {
      for (var dataset : ConfiguredDatasets.forPlant(plant, links)) {
        var test = latest.get(ApiTestHistoryService.key(plant.id, dataset));
        var row = new LinkedHashMap<String, Object>();
        row.put("name", plant.name);
        row.put("plantCode", plant.plantId);
        row.put("category", dataset);
        row.put("owner", test == null ? "—" : test.actor());
        row.put("status", test == null ? "Not tested" : test.outcome());
        row.put(
            "tone",
            test == null ? "untested" : test.outcome().equals("Success") ? "success" : "failed");
        row.put(
            "testedAt", test == null ? null : test.testedAt().atZoneSameInstant(Policies.LOCAL));
        row.put("dataUrl", "/admin/api-test-history?filterPlant=" + plant.id);
        rows.add(row);
      }
    }
    rows.sort(
        Comparator.comparing(
            row -> (java.time.ZonedDateTime) row.get("testedAt"),
            Comparator.nullsLast(Comparator.reverseOrder())));
    return rows;
  }
}
