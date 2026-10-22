package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;

class PolicyTests {
  @Test
  void locationAggregationUsesMeanForMmfAndSumForForecasts() {
    var first = new LssPlant();
    first.id = 1;
    first.state = "Selangor";
    first.capacityMw = new BigDecimal("50");
    var second = new LssPlant();
    second.id = 2;
    second.state = "Negeri Sembilan";
    second.capacityMw = new BigDecimal("40");
    var a = new SolarApiReading();
    a.plantId = 1;
    a.parameter = "AmbientTemp";
    a.value = new BigDecimal("20");
    var b = new SolarApiReading();
    b.plantId = 2;
    b.parameter = "AmbientTemp";
    b.intervalStart = a.intervalStart;
    b.value = new BigDecimal("30");
    var mmf = AnalyticsService.aggregate(List.of(first, second), List.of(a, b), "Region", true);
    assertThat(mmf.plants()).hasSize(1);
    assertThat(mmf.plants().getFirst().name).isEqualTo("Central");
    assertThat(mmf.readings().getFirst().value).isEqualByComparingTo("25");
    assertThat(
            AnalyticsService.aggregate(List.of(first, second), List.of(a, b), "Region", false)
                .readings()
                .getFirst()
                .value)
        .isEqualByComparingTo("50");
  }

  @Test
  void collectionSchedulesRespectSubmissionDaysAndLastRun() {
    var s = new CollectionSchedule();
    s.apiType = "DDQ";
    s.dataType = "Weekly";
    s.scheduleDay = "Thursday";
    s.runTime = "08:00";
    var thursday = LocalDateTime.of(2026, 10, 22, 8, 10);
    assertThat(Policies.collectionDue(s, thursday, null)).isTrue();
    assertThat(Policies.collectionDue(s, thursday, thursday.minusMinutes(5))).isFalse();
    assertThat(Policies.collectionDue(s, thursday.minusDays(1), null)).isFalse();
    assertThat(Policies.collectionDue(s, thursday.withHour(7), null)).isFalse();
    s.dataType = "4 Month";
    s.scheduleDay = "31";
    s.scheduleMonths = "2,6,10";
    assertThat(Policies.collectionDue(s, LocalDateTime.of(2026, 2, 28, 8, 0), null)).isTrue();
    s.apiType = "MMF";
    s.interval = "30 minutes";
    assertThat(Policies.collectionDue(s, thursday, thursday.minusMinutes(29))).isFalse();
    assertThat(Policies.collectionDue(s, thursday, thursday.minusMinutes(30))).isTrue();
  }

  @Test
  void rollingReportsUseStableSlotsAndWeeklyReportsDoNotRunOnWrongDay() {
    var s = new ReportScheduleEntity();
    s.scheduleType = "Rolling 24 Hour";
    s.intervalMinutes = 30;
    assertThat(Policies.reportSlot(s, LocalDateTime.of(2026, 10, 22, 8, 37)))
        .isEqualTo("2026-10-22T08:30");
    s.scheduleType = "Weekly";
    s.scheduleDay = "Thursday";
    s.executionTime = "07:00";
    assertThat(Policies.reportSlot(s, LocalDateTime.of(2026, 10, 22, 7, 0))).isNotNull();
    assertThat(Policies.reportSlot(s, LocalDateTime.of(2026, 10, 23, 7, 0))).isNull();
  }

  @Test
  void mmfGapFillRequiresBothNeighboursAndNonZeroSensorAverageMatchesOriginal() {
    assertThat(Policies.gapFill(null, new BigDecimal("3"), new BigDecimal("5")))
        .isEqualByComparingTo("4");
    assertThat(Policies.gapFill(null, null, BigDecimal.ONE)).isNull();
    assertThat(
            Policies.averageNonZero(
                List.of(BigDecimal.ZERO, new BigDecimal("2"), new BigDecimal("4"))))
        .isEqualByComparingTo("3");
  }

  @Test
  void ddqSubstitutionPrefersPreviousSubmissionThenCapacityThenZero() {
    var p = new LssPlant();
    p.id = 1;
    p.capacityMw = new BigDecimal("50");
    var now = LocalDateTime.of(2026, 10, 22, 0, 0);
    var current = new SolarApiReading();
    current.id = 2L;
    current.plantId = 1;
    current.parameter = "Value";
    current.intervalStart = now;
    current.value = new BigDecimal("70");
    var previous = new SolarApiReading();
    previous.id = 1L;
    previous.plantId = 1;
    previous.parameter = "Value";
    previous.intervalStart = now;
    previous.value = new BigDecimal("20");
    var rule = new ValidationRule();
    rule.code = "DDQ-005";
    rule.dataType = "DDQ";
    rule.fieldName = "*";
    var prepared =
        ReportService.prepare(
            "Rolling 24 Hour",
            now,
            15,
            List.of(p),
            List.of(current),
            List.of(previous),
            List.of(rule),
            now,
            now.plusMinutes(15));
    assertThat(prepared.readings().getFirst().value).isEqualByComparingTo("20");
    assertThat(prepared.substitutions().getFirst().source()).startsWith("Previous-day");
    prepared =
        ReportService.prepare(
            "Rolling 24 Hour",
            now,
            15,
            List.of(p),
            List.of(current),
            List.of(),
            List.of(rule),
            now,
            now.plusMinutes(15));
    assertThat(prepared.readings().getFirst().value).isEqualByComparingTo("50");
    prepared =
        ReportService.prepare(
            "Rolling 24 Hour",
            now,
            15,
            List.of(p),
            List.of(),
            List.of(),
            List.of(rule),
            now,
            now.plusMinutes(15));
    assertThat(prepared.readings().getFirst().value).isEqualByComparingTo("0");
  }

  @Test
  void csvFormulaFieldsAreNeutralizedButNegativeNumericDataIsPreserved() {
    assertThat(ReportService.safeCsv("  =HYPERLINK(\"bad\")")).startsWith("'");
    assertThat(ReportService.safeCsv("-4.5")).isEqualTo("-4.5");
    assertThat(ReportService.safeCsv("-CMD")).isEqualTo("'-CMD");
  }
}
