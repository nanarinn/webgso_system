package my.webgso;

import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import my.webgso.Models.SolarApiReading;

/** Evaluates source measurement times, never the locally assigned test/submission time. */
public final class ApiDataFreshness {
  private ApiDataFreshness() {}

  public record Status(String label, String latest, String age, String warning) {}

  public static Status mmfStatus(List<SolarApiReading> readings, LocalDateTime now) {
    var latest = readings.stream()
        .filter(r -> !Boolean.TRUE.equals(r.isSynthetic))
        .map(r -> r.intervalStart).filter(Objects::nonNull)
        .max(Comparator.naturalOrder()).orElse(null);
    if (latest == null)
      return new Status("Unknown", "Not available", "Not available",
          "Data freshness could not be checked: no source timestamp is available.");
    String time = latest.format(DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm:ss", Locale.ENGLISH)) + " MYT";
    long seconds = Duration.between(latest, now).getSeconds();
    if (seconds < 0)
      return new Status("Check timestamp", time, "Timestamp is in the future",
          "The MMF source timestamp is in the future. Check the publisher clock and time zone before trusting data freshness.");
    String age = (seconds / 60) + " min " + (seconds % 60) + " sec";
    boolean stale = latest.isBefore(now.minusMinutes(5));
    return new Status(stale ? "Stale" : "Fresh", time, age, stale
        ? "Stale data: the latest MMF measurement is " + time
            + ". It is older than 5 minutes. The API connected, but its data may not be updating."
        : "");
  }

  public static String warning(List<SolarApiReading> readings, String dataset, LocalDateTime now) {
    boolean mmf = "MMF".equals(dataset);
    if (mmf) return mmfStatus(readings, now).warning();
    var latest =
        readings.stream()
            .filter(r -> !Boolean.TRUE.equals(r.isSynthetic))
            .map(
                r ->
                    mmf
                        ? r.intervalStart
                        : (r.intervalEnd == null ? r.intervalStart : r.intervalEnd))
            .filter(Objects::nonNull)
            .max(Comparator.naturalOrder())
            .orElse(null);
    if (latest == null)
      return "Data freshness could not be checked: no source timestamp is available.";
    boolean stale = !latest.isAfter(now);
    if (!stale) return "";
    String time =
        latest.format(DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm", Locale.ENGLISH)) + " MYT";
    return "Expired forecast: the returned DDQ forecast coverage ended at "
            + time
            + ". The API connected, but it has no current or future forecast data.";
  }
}
