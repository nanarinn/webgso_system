package my.webgso;

import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;

public final class Policies {
  public static final ZoneId LOCAL = ZoneId.of("Asia/Kuala_Lumpur");
  public static final List<String> DDQ_TYPES =
      List.of("Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead");
  public static final Map<String, String> MMF_UNITS =
      Map.ofEntries(
          Map.entry("AirPressure", "hPa"),
          Map.entry("AmbientTemp", "C"),
          Map.entry("AtmPressure", "atm"),
          Map.entry("HorIrradiance", "W/m2"),
          Map.entry("Humidity", "%"),
          Map.entry("IncIrradiance", "W/m2"),
          Map.entry("MWPerArea", "MW"),
          Map.entry("Precipitation", "mm/h"),
          Map.entry("PVTemperature", "C"),
          Map.entry("TotRadiation", "Wh/m2"),
          Map.entry("WindDir", "o"),
          Map.entry("WindDirSD", "o"),
          Map.entry("WindSpeed", "m/s"));

  private Policies() {}

  public static String canonical(String value) {
    return switch (value.trim().toLowerCase(Locale.ROOT)) {
      case "daily", "day ahead", "five day ahead", "5 day ahead", "five-day ahead" -> "Day Ahead";
      case "weekly", "week ahead" -> "Week Ahead";
      case "4 month",
              "4 month ahead",
              "4 months ahead",
              "four month ahead",
              "four months ahead",
              "month ahead" ->
          "4 Month Ahead";
      case "mmf" -> "MMF";
      default -> "Rolling 24 Hour";
    };
  }

  public static int interval(String value) {
    return switch (value) {
      case "30 minutes" -> 30;
      case "1 hour" -> 60;
      default -> 15;
    };
  }

  public static BigDecimal average(List<BigDecimal> values) {
    return values.isEmpty()
        ? BigDecimal.ZERO
        : values.stream()
            .reduce(BigDecimal.ZERO, BigDecimal::add)
            .divide(BigDecimal.valueOf(values.size()), 6, RoundingMode.HALF_EVEN);
  }

  public static BigDecimal averageNonZero(List<BigDecimal> values) {
    return average(values.stream().filter(v -> v.signum() != 0).toList());
  }

  public static BigDecimal gapFill(BigDecimal exact, BigDecimal before, BigDecimal after) {
    return exact != null
        ? exact
        : before == null || after == null
            ? null
            : before.add(after).divide(BigDecimal.TWO, 6, RoundingMode.HALF_UP);
  }

  public static boolean collectionDue(CollectionSchedule s, LocalDateTime now, LocalDateTime last) {
    if (s.cronExpression != null && !s.cronExpression.isBlank()) {
      try {
        var next =
            org.springframework.scheduling.support.CronExpression.parse(s.cronExpression)
                .next(last == null ? now.minusMinutes(1) : last);
        return next != null && !next.isAfter(now);
      } catch (IllegalArgumentException e) {
        return false;
      }
    }
    if ("MMF".equals(s.apiType) || "MMF".equals(s.dataType))
      return last == null || Duration.between(last, now).toMinutes() >= interval(s.interval);
    LocalTime time;
    try {
      time = LocalTime.parse(s.runTime);
    } catch (Exception e) {
      return false;
    }
    if (now.toLocalTime().isBefore(time)
        || last != null
            && last.toLocalDate().equals(now.toLocalDate())
            && !last.toLocalTime().isBefore(time)) return false;
    if (s.dataType.equals("Weekly") || canonical(s.dataType).equals("Week Ahead"))
      return now.getDayOfWeek().name().equalsIgnoreCase(s.scheduleDay);
    if (s.dataType.equals("4 Month") || canonical(s.dataType).equals("4 Month Ahead")) {
      try {
        return now.getDayOfMonth()
                == Math.min(Integer.parseInt(s.scheduleDay), now.toLocalDate().lengthOfMonth())
            && months(s.scheduleMonths).contains(now.getMonthValue());
      } catch (Exception e) {
        return false;
      }
    }
    return true;
  }

  public static Set<Integer> months(String value) {
    var result = new HashSet<Integer>();
    for (String part : value.split(","))
      try {
        result.add(Integer.parseInt(part.trim()));
      } catch (NumberFormatException ignored) {
      }
    return result;
  }

  public static String reportSlot(ReportScheduleEntity s, LocalDateTime now) {
    if (s.scheduleType.equalsIgnoreCase("Rolling 24 Hour")) {
      int interval = Math.clamp(s.intervalMinutes, 1, 1440),
          minute = now.getHour() * 60 + now.getMinute();
      return now.toLocalDate().atStartOfDay().plusMinutes(minute - minute % interval).toString();
    }
    LocalTime time;
    try {
      time = LocalTime.parse(s.executionTime);
    } catch (Exception e) {
      return null;
    }
    if (now.toLocalTime().isBefore(time)) return null;
    if (s.scheduleType.equalsIgnoreCase("Weekly")
        && !now.getDayOfWeek().name().equalsIgnoreCase(s.scheduleDay)) return null;
    if (s.scheduleType.equalsIgnoreCase("4 Month")) {
      try {
        if (now.getDayOfMonth() != Integer.parseInt(s.scheduleDay)
            || !months(s.scheduleMonths).contains(now.getMonthValue())) return null;
      } catch (Exception e) {
        return null;
      }
    }
    return now.toLocalDate().atTime(time).toString();
  }
}
