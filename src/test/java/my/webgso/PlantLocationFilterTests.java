package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.util.*;
import my.webgso.Models.LssPlant;
import org.junit.jupiter.api.Test;

class PlantLocationFilterTests {
  @Test
  void filtersOnlyTheChosenDimensionAndSupportsSeveralValues() {
    var p = new LssPlant();
    p.id = 18;
    p.state = "PERAK";
    p.zone = "Zone 2";
    assertThat(new PlantLocationFilter("plant", List.of("17", "18")).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("plant", List.of("17")).matches(p)).isFalse();
    assertThat(new PlantLocationFilter("state", List.of("Johor", "Perak")).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("region", List.of("Northern")).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("region", List.of("Southern")).matches(p)).isFalse();
    assertThat(new PlantLocationFilter("zone", List.of("zone 2")).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("zone", List.of("Zone 1")).matches(p)).isFalse();
    assertThat(new PlantLocationFilter("all", List.of("missing")).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("state", List.of()).matches(p)).isTrue();
    assertThat(new PlantLocationFilter("zone", List.of("Zone 2", "Zone 2", "")).values())
        .containsExactly("Zone 2");
    assertThatThrownBy(() -> new PlantLocationFilter("invalid", List.of()))
        .isInstanceOf(IllegalArgumentException.class);
  }
}
