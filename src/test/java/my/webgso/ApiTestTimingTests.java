package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.time.*;
import my.webgso.Models.CollectionSchedule;
import org.junit.jupiter.api.Test;

class ApiTestTimingTests {
  @Test
  void followsMeasurementIntervalAndCronInScheduleTimezone() {
    var schedule = new CollectionSchedule();
    schedule.apiType = "MMF";
    schedule.interval = "30 minutes";
    var last = OffsetDateTime.parse("2026-10-29T02:07:00Z");
    assertThat(ApiTestTiming.next(schedule, last).toInstant())
        .isEqualTo(last.plusMinutes(30).toInstant());
    schedule.cronExpression = "0 */15 * * * *";
    assertThat(ApiTestTiming.next(schedule, last).toInstant())
        .isEqualTo(Instant.parse("2026-10-29T02:15:00Z"));
  }

  @Test
  void followsDailyWeeklyAndFourMonthCalendarRuns() {
    var schedule = new CollectionSchedule();
    schedule.runTime = "09:00";
    var last = OffsetDateTime.parse("2026-10-29T10:07:00+08:00");
    assertThat(ApiTestTiming.next(schedule, last))
        .isEqualTo(OffsetDateTime.parse("2026-10-30T09:00:00+08:00"));
    schedule.dataType = "Week Ahead";
    schedule.scheduleDay = "MONDAY";
    assertThat(ApiTestTiming.next(schedule, last))
        .isEqualTo(OffsetDateTime.parse("2026-11-02T09:00:00+08:00"));
    schedule.dataType = "4 Month Ahead";
    schedule.scheduleDay = "31";
    schedule.scheduleMonths = "2,6,10";
    last = OffsetDateTime.parse("2026-10-31T10:00:00+08:00");
    assertThat(ApiTestTiming.next(schedule, last))
        .isEqualTo(OffsetDateTime.parse("2027-02-28T09:00:00+08:00"));
    schedule.cronExpression = "invalid";
    assertThat(ApiTestTiming.next(schedule, last)).isNull();
  }
}
