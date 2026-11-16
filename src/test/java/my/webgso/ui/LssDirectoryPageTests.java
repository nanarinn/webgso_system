package my.webgso.ui;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import java.util.Map;
import my.webgso.Models.LssPlant;
import my.webgso.Models.PlantApiConfiguration;
import org.junit.jupiter.api.Test;

class LssDirectoryPageTests {
  @Test
  void separatesSavedDatasetsAndKeepsUnconfiguredPlantsVisible() {
    var kmpr = plant(101, "KMPR");
    var bdns = plant(102, "BDNS");
    var both = plant(103, "Both datasets");
    both.ddqApiEndpoint = "https://example.test/forecast";
    both.mmfApiEndpoint = "https://example.test/weather";
    var pending = plant(104, "Pending connection");
    String html = LssDirectoryPage.render(new PageContext(Map.of(
        "plants", List.of(kmpr, bdns, both, pending), "isAdmin", true,
        "apiLinks", List.of(link(101, "DDQ", "Day Ahead", "https://example.test/day"),
            link(102, "MMF", "MMF", "https://example.test/mmf"),
            link(104, "DDQ", "Day Ahead", ""))), null)).render();
    String ddq = html.substring(html.indexOf("<h2>DDQ plants"), html.indexOf("<h2>MMF plants"));
    String mmf = html.substring(html.indexOf("<h2>MMF plants"), html.indexOf("<h2>Plants without APIs"));
    assertThat(ddq).contains("KMPR", "Both datasets").doesNotContain("BDNS", "Pending connection");
    assertThat(mmf).contains("BDNS", "Both datasets").doesNotContain("KMPR", "Pending connection");
    assertThat(html.substring(html.indexOf("<h2>Plants without APIs"))).contains("Pending connection");
  }

  @Test
  void userDirectoryHasNoEditingActionsAndShowsEmptyCategories() {
    String html = LssDirectoryPage.render(new PageContext(Map.of("plants", List.of(plant(1, "Pending"))), null)).render();
    assertThat(html).contains("DDQ plants", "MMF plants", "Read only", "/user/plant-config")
        .doesNotContain("/admin/lss-plants/edit", "Add plant", "View / Edit");
  }

  private LssPlant plant(int id, String name) {
    var plant = new LssPlant();
    plant.id = id;
    plant.name = name;
    return plant;
  }

  private PlantApiConfiguration link(int plantId, String apiType, String dataType, String url) {
    var link = new PlantApiConfiguration();
    link.plantId = plantId;
    link.apiType = apiType;
    link.dataType = dataType;
    link.apiLink = url;
    return link;
  }
}
