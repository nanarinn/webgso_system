package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.time.*;
import java.util.*;
import my.webgso.Models.SolarApiReading;
import org.junit.jupiter.api.Test;

class ApiDataFreshnessTests {
  final LocalDateTime now = LocalDateTime.of(2026, 11, 2, 12, 0);

  SolarApiReading reading(LocalDateTime start) {
    var r = new SolarApiReading();
    r.intervalStart = start;
    r.intervalEnd = start.plusMinutes(15);
    // This is locally assigned at import/test time and must not hide stale source data.
    r.submissionDateTime = now;
    return r;
  }

  @Test
  void oldMmfWarnsButRecentAndExactBoundaryDoNot() {
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusMinutes(61))), "MMF", now))
        .contains("Stale data", "10:59", "60 minutes");
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusMinutes(60))), "MMF", now))
        .isEmpty();
    assertThat(
            ApiDataFreshness.warning(
                List.of(reading(now.minusDays(1)), reading(now.minusMinutes(5))), "MMF", now))
        .isEmpty();
  }

  @Test
  void forecastsAreCheckedByCoverageNotDistanceIntoFuture() {
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusMinutes(15))), "Day Ahead", now))
        .contains("Expired forecast");
    assertThat(
            ApiDataFreshness.warning(List.of(reading(now.minusMinutes(5))), "Rolling 24 Hour", now))
        .isEmpty();
    assertThat(ApiDataFreshness.warning(List.of(reading(now.plusMonths(4))), "4 Month Ahead", now))
        .isEmpty();
  }

  @Test
  void syntheticValuesCannotHideStaleSourceAndMissingIsNotFresh() {
    var synthetic = reading(now);
    synthetic.isSynthetic = true;
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusHours(2)), synthetic), "MMF", now))
        .contains("Stale data");
    assertThat(ApiDataFreshness.warning(List.of(), "MMF", now)).contains("could not be checked");
  }
}
