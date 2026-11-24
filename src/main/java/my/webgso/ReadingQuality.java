package my.webgso;

import java.math.BigDecimal;
import java.time.Duration;
import java.time.LocalDateTime;
import java.util.Locale;
import my.webgso.Models.LssPlant;
import my.webgso.Models.SolarApiReading;

/** Conservative, display-only health checks for readings in the isolated data viewer. */
public final class ReadingQuality {
  private ReadingQuality() {}

  public record Result(String tone, String label, String reason) {
    public boolean issue() {
      return !"normal".equals(tone);
    }
  }

  public static Result inspect(SolarApiReading reading, LssPlant plant, LocalDateTime now) {
    if (reading == null) return error("Reading is missing");
    if (reading.errorCode != null && reading.errorCode != 0)
      return error("API error " + reading.errorCode + messageSuffix(reading.errorMessage));
    if (reading.errorMessage != null
        && !reading.errorMessage.isBlank()
        && !"OK".equalsIgnoreCase(reading.errorMessage.trim()))
      return error(reading.errorMessage.trim());
    if (reading.parameter == null || reading.parameter.isBlank())
      return error("Parameter name is missing");
    if (reading.value == null) return error("Numeric value is missing");

    String parameter =
        reading.parameter.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]", "");
    BigDecimal value = reading.value;
    Result range = rangeProblem(parameter, value);
    if (range != null) return range;

    if (!"MMF".equalsIgnoreCase(reading.dataType)) {
      if (value.signum() < 0) return error("Forecast value is negative");
      if (plant != null
          && plant.capacityMw != null
          && plant.capacityMw.signum() > 0
          && value.compareTo(plant.capacityMw) > 0)
        return warning("Value exceeds registered plant capacity");
    }

    if ("MMF".equalsIgnoreCase(reading.dataType) && reading.intervalStart != null) {
      if (reading.intervalStart.isAfter(now.plusMinutes(15)))
        return warning("Measurement timestamp is in the future");
      if (Duration.between(reading.intervalStart, now).toMinutes() > 120)
        return warning("Measurement is more than 2 hours old");
    }
    if (Boolean.TRUE.equals(reading.isSynthetic))
      return warning(
          reading.syntheticReason == null || reading.syntheticReason.isBlank()
              ? "Synthetic or substituted value"
              : reading.syntheticReason);
    return new Result("normal", "Normal", "Passed basic data checks");
  }

  private static Result rangeProblem(String parameter, BigDecimal value) {
    if (is(parameter, "humidity", "humid", "rh") && outside(value, 0, 100))
      return error("Humidity must be between 0 and 100%");
    if (is(parameter, "winddirection", "winddir", "wd") && outside(value, 0, 360))
      return error("Wind direction must be between 0 and 360 degrees");
    if (is(parameter, "windspeed", "ws") && outside(value, 0, 75))
      return error("Wind speed is outside the expected 0-75 range");
    if (is(parameter, "ambienttemperature", "ambtemp", "temperature", "temp")
        && outside(value, -50, 70))
      return error("Temperature is outside the expected -50 to 70 C range");
    if (is(parameter, "precipitation", "precip", "rain", "rainfall") && value.signum() < 0)
      return error("Precipitation cannot be negative");
    if (is(parameter, "irradiance", "inclirradiance", "tgi", "tghi", "poa")
        && value.signum() < 0)
      return warning("Irradiance is negative");
    return null;
  }

  private static boolean is(String actual, String... candidates) {
    for (String candidate : candidates)
      if (candidate.equals(actual) || actual.endsWith(candidate)) return true;
    return false;
  }

  private static boolean outside(BigDecimal value, double minimum, double maximum) {
    return value.compareTo(BigDecimal.valueOf(minimum)) < 0
        || value.compareTo(BigDecimal.valueOf(maximum)) > 0;
  }

  private static String messageSuffix(String message) {
    return message == null || message.isBlank() ? "" : ": " + message.trim();
  }

  private static Result error(String reason) {
    return new Result("error", "Error", reason);
  }

  private static Result warning(String reason) {
    return new Result("warning", "Warning", reason);
  }
}
