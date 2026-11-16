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
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusMinutes(6))), "MMF", now))
        .contains("Stale data", "11:54", "5 minutes");
    assertThat(ApiDataFreshness.warning(List.of(reading(now.minusMinutes(5))), "MMF", now))
        .isEmpty();
    assertThat(
            ApiDataFreshness.warning(
                List.of(reading(now.minusDays(1)), reading(now.minusMinutes(5))), "MMF", now))
        .isEmpty();
  }

  @Test
  void displaysSourceTimeAndAgeAndChecksSecondsAtBoundary() {
    var fresh = ApiDataFreshness.mmfStatus(List.of(reading(now.minusSeconds(299))), now);
    assertThat(fresh.label()).isEqualTo("Fresh");
    assertThat(fresh.age()).isEqualTo("4 min 59 sec");
    assertThat(fresh.latest()).contains("02 Nov 2026, 11:55:01 MYT");
    assertThat(ApiDataFreshness.mmfStatus(List.of(reading(now.minusSeconds(301))), now).label())
        .isEqualTo("Stale");
    var future = ApiDataFreshness.mmfStatus(List.of(reading(now.plusMinutes(1))), now);
    assertThat(future.label()).isEqualTo("Check timestamp");
    assertThat(future.warning()).contains("publisher clock");
    assertThat(ApiDataFreshness.mmfStatus(List.of(), now).label()).isEqualTo("Unknown");
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
