package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class PlantZoneTests {
  @Test
  void assignsZonesForEveryStateIncludingLegacyNames() {
    for (String state : new String[] {"Perak", "Kedah", "Perlis", "Pulau Pinang", "Penang"})
      assertThat(PlantZone.forState(state)).isEqualTo("Northern");
    for (String state : new String[] {"Selangor", "Negeri Sembilan", "Kuala Lumpur", "Putrajaya"})
      assertThat(PlantZone.forState(state)).isEqualTo("Central");
    for (String state : new String[] {"Johor", "Melaka"})
      assertThat(PlantZone.forState(state)).isEqualTo("Southern");
    for (String state : new String[] {"Pahang", "Kelantan", "Terengganu"})
      assertThat(PlantZone.forState(state)).isEqualTo("Eastern");
    assertThat(PlantZone.forState("Sabah")).isEqualTo("Sabah");
    assertThat(PlantZone.forState("Labuan")).isEqualTo("Sabah");
    assertThat(PlantZone.forState("Sarawak")).isEqualTo("Sarawak");
    assertThat(PlantZone.forState(" PERAK ")).isEqualTo("Northern");
    assertThat(PlantZone.forState(null)).isEmpty();
    assertThat(PlantZone.forState("Unknown")).isEmpty();
  }
}
