package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;

class ConfiguredDatasetsTests {
  @Test
  void onlyOffersNonblankUrlsForTheSelectedPlantAndMatchingApiType() {
    var plant = new LssPlant();
    plant.id = 16;
    var day = link(16, "DDQ", "Day Ahead", "https://example.test/dayahead");
    var rolling = link(16, "DDQ", "Rolling 24 Hour", "  ");
    var other = link(17, "DDQ", "Week Ahead", "https://example.test/weekahead");
    var wrongType = link(16, "MMF", "4 Month Ahead", "https://example.test/fourmonthsahead");
    assertThat(ConfiguredDatasets.forPlant(plant, List.of(day, rolling, other, wrongType)))
        .containsExactly("Day Ahead");
    day.apiLink = "";
    assertThat(ConfiguredDatasets.forPlant(plant, List.of(day))).isEmpty();
  }

  @Test
  void retainsExplicitGeneralEndpointSupportAndSeparatesMmf() {
    var plant = new LssPlant();
    plant.id = 1;
    plant.mmfApiEndpoint = "https://example.test/live/mmf";
    assertThat(ConfiguredDatasets.forPlant(plant, List.of())).containsExactly("MMF");
    plant.ddqApiEndpoint = "https://example.test/forecast";
    assertThat(ConfiguredDatasets.forPlant(plant, List.of()))
        .containsExactlyElementsOf(ConfiguredDatasets.ALL);
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
