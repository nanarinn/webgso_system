package my.webgso;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import org.apache.commons.csv.*;
import org.springframework.core.env.Environment;
import org.springframework.stereotype.Service;

@Service
public class ApiBatchReports {
  private final MailDelivery mail;
  private final Environment env;

  public ApiBatchReports(MailDelivery mail, Environment env) {
    this.mail = mail;
    this.env = env;
  }

  public static byte[] csv(ApiRetestService.Job job) {
    var text = new StringWriter();
    text.write('\ufeff');
    try (var csv = new CSVPrinter(text, CSVFormat.DEFAULT)) {
      csv.printRecord(
          "Check ID",
          "Completed at (MYT)",
          "Plant ID (system)",
          "Plant name",
          "API",
          "Result",
          "HTTP status",
          "Response time (ms)",
          "Message");
      for (var item : job.items)
        csv.printRecord(
            job.id,
            job.finishedAt == null
                ? ""
                : job.finishedAt.atZoneSameInstant(Policies.LOCAL).toString(),
            item.plantId(),
            ApiHistoryCsv.safeText(item.plantName()),
            ApiHistoryCsv.safeText(item.dataset()),
            ApiHistoryCsv.safeText(item.status()),
            item.httpStatus(),
            item.responseMillis(),
            ApiHistoryCsv.safeText(item.message()));
    } catch (IOException e) {
      throw new IllegalStateException("Could not create API results CSV.", e);
    }
    return text.toString().getBytes(StandardCharsets.UTF_8);
  }

  public void automatically(ApiRetestService.Job job) {
    if (env.getProperty("webgso.api-alerts.enabled", Boolean.class, false)
        && job.items.stream().allMatch(item -> item.status().equals("Success"))) send(job);
  }

  public void send(ApiRetestService.Job job) {
    synchronized (job) {
      if (job.reportStatus.equals("Sent") || job.reportStatus.equals("Sending")) return;
      if (job.items.isEmpty()
          || job.items.stream()
              .anyMatch(item -> !Set.of("Success", "Failed").contains(item.status())))
        throw new IllegalArgumentException(
            "Wait until all API tests finish before emailing results.");
      if (!env.getProperty("webgso.mail.enabled", Boolean.class, false)
          || env.getProperty("webgso.api-alerts.recipient", "").isBlank()) {
        job.reportStatus = "Not configured";
        return;
      }
      job.reportStatus = "Sending";
    }
    try {
      String recipient = env.getProperty("webgso.api-alerts.recipient");
      long passed = job.items.stream().filter(item -> item.status().equals("Success")).count();
      mail.send(
          List.of(recipient),
          "GSO Web Services - API test results",
          passed
              + " of "
              + job.items.size()
              + " APIs passed. The attached Excel workbook (.xlsx) contains the API test results"
              + " from this run. Times use Malaysia Standard Time (UTC+8).",
          new ReportService.Download(
              ApiResultsExcel.create(job),
              "gso-api-test-results.xlsx",
              ApiResultsExcel.CONTENT_TYPE));
      job.reportStatus = "Sent";
    } catch (RuntimeException e) {
      job.reportStatus = "Failed";
    }
  }
}
