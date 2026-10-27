package my.webgso;

import java.math.*;
import java.time.*;
import java.util.*;
import org.springframework.context.annotation.Profile;
import org.springframework.web.bind.annotation.*;

/** Local development fixture; this controller is absent from the production profile. */
@RestController
@Profile({"local", "demo"})
public class TestDataController {
  @GetMapping("/api/test-data/mmf/{plantCode}")
  public Map<String, Object> mmf(
      @PathVariable String plantCode,
      @RequestParam(required = false) LocalDate date,
      @RequestParam(defaultValue = "15") int interval) {
    if (!Set.of(15, 30, 60).contains(interval))
      throw new IllegalArgumentException("Use 15, 30 or 60 minutes");
    if (!plantCode.matches("[A-Za-z0-9-]{1,40}"))
      throw new IllegalArgumentException("Invalid plant code");
    var start = (date == null ? LocalDate.now(Policies.LOCAL) : date).atStartOfDay();
    var rows = new ArrayList<Map<String, Object>>();
    for (int period = 0; period < 1440 / interval; period++) {
      double hour = period * interval / 60.0,
          solar = Math.max(0, Math.sin(Math.PI * (hour - 6) / 12));
      for (var parameter : Policies.MMF_UNITS.entrySet())
        for (int sensor = 1; sensor <= 5; sensor++) {
          double factor = 1 + (sensor - 3) * .008, offset = (sensor - 3) * .12;
          double value =
              switch (parameter.getKey()) {
                case "AmbientTemp" -> 24 + 8 * solar + offset;
                case "AtmPressure" -> (.998 - .003 * solar) * factor;
                case "AirPressure" -> (1012 - 4 * solar) * factor;
                case "HorIrradiance" -> 950 * solar * factor;
                case "Humidity" -> (82 - 35 * solar) * factor;
                case "IncIrradiance" -> 1020 * solar * factor;
                case "MWPerArea" -> 50 * solar * factor;
                case "Precipitation" -> hour >= 16 && hour < 17 ? .2 * factor : 0;
                case "PVTemperature" -> 24 + 25 * solar + offset;
                case "TotRadiation" -> 950 * solar * interval / 60 * factor;
                case "WindDir" -> (110 + hour * 6 + (sensor - 3) * 2) % 360;
                case "WindDirSD" -> 5 + 2 * solar + Math.abs(offset);
                case "WindSpeed" -> (2 + 3 * solar) * factor;
                default -> 0;
              };
          rows.add(
              Map.of(
                  "timestamp",
                  start.plusMinutes((long) period * interval),
                  "parameter",
                  parameter.getKey(),
                  "value",
                  BigDecimal.valueOf(Math.max(0, value)).setScale(6, RoundingMode.HALF_EVEN),
                  "unit",
                  parameter.getValue(),
                  "sensor",
                  "MMF" + sensor));
        }
    }
    return Map.of(
        "source",
        "GSO sample MMF test API",
        "plantCode",
        plantCode.toUpperCase(Locale.ROOT),
        "submissionDateTime",
        LocalDateTime.now(Policies.LOCAL),
        "intervalMinutes",
        interval,
        "sensorReadings",
        rows);
  }
}
