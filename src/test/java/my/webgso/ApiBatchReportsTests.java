package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.nio.charset.StandardCharsets;
import java.util.*;
import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;

class ApiBatchReportsTests {
  private ApiRetestService.Job job(String status) {
    return new ApiRetestService.Job(
        "owner",
        List.of(
            new ApiRetestService.Item(
                1, "=UNSAFE, Plant", "Day Ahead", status, 200, 10L, "Line one\nLine two")));
  }

  @Test
  void exportsExactJobResultsWithExcelSafeCells() {
    var result = new String(ApiBatchReports.csv(job("Success")), StandardCharsets.UTF_8);
    assertThat(result)
        .startsWith("\ufeff")
        .contains("'=UNSAFE", "Day Ahead", "Success", "\"Line one\nLine two\"");
  }

  @Test
  void allSuccessfulEmailsCsvOnlyOnceAndMixedResultsRequireManualSend() {
    var mail = mock(MailDelivery.class);
    var env =
        new MockEnvironment()
            .withProperty("webgso.mail.enabled", "true")
            .withProperty("webgso.api-alerts.enabled", "true")
            .withProperty("webgso.api-alerts.recipient", "admin@example.test");
    var reports = new ApiBatchReports(mail, env);
    var success = job("Success");
    reports.automatically(success);
    reports.send(success);
    var attachment = org.mockito.ArgumentCaptor.forClass(ReportService.Download.class);
    verify(mail)
        .send(eq(List.of("admin@example.test")), anyString(), anyString(), attachment.capture());
    assertThat(attachment.getValue().filename()).endsWith(".csv");
    assertThat(success.reportStatus).isEqualTo("Sent");
    var failed = job("Failed");
    reports.automatically(failed);
    verifyNoMoreInteractions(mail);
    reports.send(failed);
    assertThat(failed.reportStatus).isEqualTo("Sent");
  }

  @Test
  void pendingTestsAreRejectedAndDeliveryFailureCanBeRetried() {
    var mail = mock(MailDelivery.class);
    var env =
        new MockEnvironment()
            .withProperty("webgso.mail.enabled", "true")
            .withProperty("webgso.api-alerts.recipient", "admin@example.test");
    var reports = new ApiBatchReports(mail, env);
    assertThatThrownBy(() -> reports.send(job("Running")))
        .isInstanceOf(IllegalArgumentException.class);
    doThrow(new IllegalArgumentException("SMTP failed"))
        .when(mail)
        .send(anyList(), anyString(), anyString(), any());
    var job = job("Success");
    reports.send(job);
    assertThat(job.reportStatus).isEqualTo("Failed");
    doNothing().when(mail).send(anyList(), anyString(), anyString(), any());
    reports.send(job);
    assertThat(job.reportStatus).isEqualTo("Sent");
  }
}
