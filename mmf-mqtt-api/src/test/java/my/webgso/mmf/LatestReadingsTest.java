package my.webgso.mmf;

import static org.junit.jupiter.api.Assertions.*;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import org.junit.jupiter.api.Test;

class LatestReadingsTest {
  private final Instant now = Instant.parse("2026-11-04T04:13:01Z");
  private int accept(LatestReadings store, String suffix, String body) throws Exception {
    return store.accept("LSS_BIDOR_30MW-NEDA/" + suffix, body.getBytes(StandardCharsets.UTF_8), now);
  }
  @Test void stationObjectAndSensorTopicsKeepSeparateStations() throws Exception {
    var store = new LatestReadings("LSS_BIDOR_30MW-NEDA");
    assertEquals(1, accept(store, "Weather_Station_SAE_1", "{\"Wind_speed__WSP_\":{\"timestamp\":\"2026-11-04T12:13:01+08:00\",\"unit\":\"m/s\",\"value\":67}}"));
    assertEquals(1, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", "{\"timestamp\":\"2026-11-04T12:13:01+08:00\",\"unit\":\"m/s\",\"value\":23}"));
    assertEquals(2, store.snapshot(null, now).size());
    assertEquals("WindSpeed", store.snapshot("Weather_Station_SAE_3", now).getFirst().get("parameter"));
    assertEquals("Fresh", store.snapshot(null, now.plusSeconds(300)).getFirst().get("status"));
    assertEquals("Stale", store.snapshot(null, now.plusSeconds(301)).getFirst().get("status"));
  }
  @Test void rejectsOlderDuplicatesInvalidFutureAndOtherEquipment() throws Exception {
    var store = new LatestReadings("LSS_BIDOR_30MW-NEDA");
    String body = "{\"timestamp\":\"2026-11-04T12:13:01+08:00\",\"value\":23}";
    assertEquals(1, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", body));
    assertEquals(0, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", body));
    assertEquals(0, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", body.replace("12:13:01", "12:12:01")));
    assertEquals(0, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", body.replace("12:13:01", "12:23:01")));
    assertEquals(0, accept(store, "PPC/Wind_speed__WSP_", body));
    assertEquals(0, accept(store, "Weather_Station_SAE_3/Wind_speed__WSP_", "{\"value\":23}"));
    assertEquals(1, store.snapshot(null, now).size());
    assertTrue(store.snapshot("Weather_Station_SAE_2", now).isEmpty());
  }
  @Test void recognizesScreenshotNames() {
    assertEquals("HorIrradiance", LatestReadings.parameter("Global_Horizontol_Irradiation__GHI_"));
    assertEquals("TotRadiation", LatestReadings.parameter("_TGI_"));
    assertEquals("PVTemperature", LatestReadings.parameter("PV_module_temperature"));
    assertNull(LatestReadings.parameter("Unknown_sensor"));
  }
}
