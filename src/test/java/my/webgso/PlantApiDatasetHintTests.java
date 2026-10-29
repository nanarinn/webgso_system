package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class PlantApiDatasetHintTests {
  @Test
  void warnsOnlyForAnExplicitlyRecognizedDifferentEndpointName() {
    assertThat(
            PlantApiSlots.datasetWarning(
                "https://example.test/forecast/weekahead", "Rolling 24 Hour"))
        .contains("Week Ahead", "Rolling 24 Hour");
    assertThat(PlantApiSlots.datasetWarning("https://example.test/live/mmf/", "Day Ahead"))
        .contains("MMF");
    assertThat(
            PlantApiSlots.datasetWarning(
                "https://weekahead.example.test/forecast?type=mmf", "Day Ahead"))
        .isEmpty();
    assertThat(PlantApiSlots.datasetWarning("https://example.test/forecast/dayahead", "Day Ahead"))
        .isEmpty();
    assertThat(PlantApiSlots.datasetWarning("invalid url", "Day Ahead")).isEmpty();
  }
}
