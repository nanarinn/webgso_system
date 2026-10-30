package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.io.ByteArrayInputStream;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import org.apache.poi.ss.usermodel.CellType;
import org.apache.poi.ss.usermodel.DateUtil;
import org.apache.poi.xssf.usermodel.XSSFWorkbook;
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
  void allSuccessfulEmailsExcelOnlyOnceAndMixedResultsRequireManualSend() throws Exception {
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
    assertThat(attachment.getValue().filename()).endsWith(".xlsx");
    assertThat(attachment.getValue().contentType()).isEqualTo(ApiResultsExcel.CONTENT_TYPE);
    try (var workbook = new XSSFWorkbook(new ByteArrayInputStream(attachment.getValue().data()))) {
      assertThat(workbook.getSheetAt(0).getRow(6).getCell(1).getStringCellValue())
          .isEqualTo("=UNSAFE, Plant");
      assertThat(workbook.getSheetAt(0).getRow(6).getCell(1).getCellType())
          .isEqualTo(CellType.STRING);
    }
    assertThat(success.reportStatus).isEqualTo("Sent");
    var failed = job("Failed");
    reports.automatically(failed);
    verifyNoMoreInteractions(mail);
    reports.send(failed);
    assertThat(failed.reportStatus).isEqualTo("Sent");
  }

  @Test
  void excelPreservesTypedValuesMissingResponsesAndMalaysiaCompletionTime() throws Exception {
    var job =
        new ApiRetestService.Job(
            "owner",
            List.of(
                new ApiRetestService.Item(
                    1, "KMPR", "Day Ahead", "Success", 200, 0L, "Line one\nLine two"),
                new ApiRetestService.Item(2, "BDNS", "MMF", "Failed", null, null, "No response")));
    job.finishedAt = OffsetDateTime.parse("2026-10-30T03:00:00Z");
    try (var workbook = new XSSFWorkbook(new ByteArrayInputStream(ApiResultsExcel.create(job)))) {
      assertThat(workbook.getNumberOfSheets()).isEqualTo(1);
      var sheet = workbook.getSheetAt(0);
      assertThat(sheet.getRow(1).getCell(1).getStringCellValue()).isEqualTo(job.id);
      assertThat(DateUtil.getLocalDateTime(sheet.getRow(2).getCell(1).getNumericCellValue()))
          .isEqualTo(LocalDateTime.of(2026, 10, 30, 11, 0));
      assertThat(sheet.getRow(3).getCell(1).getNumericCellValue()).isEqualTo(2);
      assertThat(sheet.getRow(3).getCell(3).getNumericCellValue()).isEqualTo(1);
      assertThat(sheet.getRow(3).getCell(5).getNumericCellValue()).isEqualTo(1);
      assertThat(sheet.getRow(6).getCell(4).getCellType()).isEqualTo(CellType.NUMERIC);
      assertThat(sheet.getRow(6).getCell(5).getNumericCellValue()).isZero();
      assertThat(sheet.getRow(7).getCell(4).getCellType()).isEqualTo(CellType.BLANK);
      assertThat(sheet.getRow(7).getCell(5).getCellType()).isEqualTo(CellType.BLANK);
      assertThat(sheet.getRow(6).getCell(6).getStringCellValue()).isEqualTo("Line one\nLine two");
      assertThat(sheet.getPaneInformation().isFreezePane()).isTrue();
      assertThat(sheet.getCTWorksheet().getAutoFilter().getRef()).isEqualTo("A6:G8");
      assertThat(workbook.getExternalLinksTable()).isEmpty();
    }
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
