package my.webgso;

import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import my.webgso.Models.SolarApiReading;

/** Evaluates source measurement times, never the locally assigned test/submission time. */
public final class ApiDataFreshness {
  private ApiDataFreshness() {}

  public static String warning(List<SolarApiReading> readings, String dataset, LocalDateTime now) {
    boolean mmf = "MMF".equals(dataset);
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
    boolean stale = mmf ? latest.isBefore(now.minusMinutes(60)) : !latest.isAfter(now);
    if (!stale) return "";
    String time =
        latest.format(DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm", Locale.ENGLISH)) + " MYT";
    return mmf
        ? "Stale data: the latest MMF measurement is "
            + time
            + ". It is older than 60 minutes. The API connected, but its data may not be updating."
        : "Expired forecast: the returned DDQ forecast coverage ended at "
            + time
            + ". The API connected, but it has no current or future forecast data.";
  }
}
