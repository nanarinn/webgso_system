package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.time.OffsetDateTime;
import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;

class ApiOverviewTests {
  @Test
  void countsConfiguredDatasetsIndividuallyAndIgnoresDeletedOrRemovedApis() {
    var plant = new LssPlant();
    plant.id = 1;
    plant.name = "KMPR";
    plant.plantId = "01";
    var links = new ArrayList<PlantApiConfiguration>();
    for (String dataset : List.of("Day Ahead", "Week Ahead", "MMF")) {
      var link = new PlantApiConfiguration();
      link.plantId = 1;
      link.apiType = dataset.equals("MMF") ? "MMF" : "DDQ";
      link.dataType = dataset;
      link.apiLink = "https://example.test/api";
      links.add(link);
    }
    var latest = new HashMap<String, ApiTestHistoryService.Entry>();
    latest.put("1:Day Ahead", entry(1, "Day Ahead", "Success"));
    latest.put("1:Week Ahead", entry(1, "Week Ahead", "Failed"));
    latest.put("1:Rolling 24 Hour", entry(1, "Rolling 24 Hour", "Success"));
    latest.put("99:MMF", entry(99, "MMF", "Success"));
    var rows = ApiOverview.rows(List.of(plant), links, latest);
    assertThat(rows).hasSize(3);
    assertThat(rows)
        .extracting(row -> row.get("tone"))
        .containsExactlyInAnyOrder("success", "failed", "untested");
    links.removeFirst();
    assertThat(ApiOverview.rows(List.of(plant), links, latest)).hasSize(2);
    assertThat(ApiOverview.rows(List.of(), links, latest)).isEmpty();
  }

  private static ApiTestHistoryService.Entry entry(int plant, String dataset, String outcome) {
    return new ApiTestHistoryService.Entry(
        1, plant, dataset, OffsetDateTime.now(), "admin", outcome, 200, 1L, 1L, "Result");
  }
}
