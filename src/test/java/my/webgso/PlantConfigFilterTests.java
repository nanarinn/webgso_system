package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;

class PlantConfigFilterTests {
  @Test
  void combinesTypeAndLatestResultOnTheSameApi() {
    var p = new LssPlant();
    p.id = 1;
    var day = new PlantApiConfiguration();
    day.plantId = 1;
    day.dataType = "Day Ahead";
    day.apiLink = "https://example.test/day";
    var mmf = new PlantApiConfiguration();
    mmf.plantId = 1;
    mmf.dataType = "MMF";
    mmf.apiLink = "https://example.test/mmf";
    var latest =
        Map.of(
            "1:Day Ahead",
            new ApiTestHistoryService.Entry(
                1,
                1,
                "Day Ahead",
                java.time.OffsetDateTime.now(),
                "admin",
                "Success",
                200,
                1L,
                1L,
                "OK"));
    assertThat(new PlantConfigFilter(1, "DDQ", "Success").matches(p, List.of(day, mmf), latest))
        .isTrue();
    assertThat(new PlantConfigFilter(1, "MMF", "Success").matches(p, List.of(day, mmf), latest))
        .isFalse();
    assertThat(new PlantConfigFilter(1, "MMF", "Not tested").matches(p, List.of(day, mmf), latest))
        .isTrue();
    assertThat(new PlantConfigFilter(2, "", "").matches(p, List.of(day, mmf), latest)).isFalse();
    assertThat(new PlantConfigFilter(0, "", "Failed").matches(p, List.of(), latest)).isFalse();
    assertThat(new PlantConfigFilter(0, "", "").matches(p, List.of(), latest)).isTrue();
    assertThatThrownBy(() -> new PlantConfigFilter(0, "invalid", ""))
        .isInstanceOf(IllegalArgumentException.class);
  }
}
