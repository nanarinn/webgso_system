package my.webgso;

import java.io.*;
import java.math.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.apache.commons.csv.*;
import org.apache.poi.ss.usermodel.*;
import org.apache.poi.xssf.usermodel.XSSFWorkbook;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.stereotype.Service;

@Service
public class ReportService {
  public record Substitution(
      int plantId,
      String parameter,
      LocalDateTime timestamp,
      BigDecimal original,
      BigDecimal value,
      String reason,
      String source) {}

  public record Prepared(List<SolarApiReading> readings, List<Substitution> substitutions) {}

  public record Download(byte[] data, String filename, String contentType) {}

  private record ExportData(List<LssPlant> plants, Prepared prepared, List<DataAnomaly> detected) {}

  private final Store db;
  private final ReadingService readings;
  private final ValidationService validation;
  private final Accounts accounts;
  private final Path directory = Path.of(".reports").toAbsolutePath().normalize();

  public ReportService(
      Store db, ReadingService readings, ValidationService validation, Accounts accounts) {
    this.db = db;
    this.readings = readings;
    this.validation = validation;
    this.accounts = accounts;
  }

  public ReportExecution generate(
      String dataType,
      Set<Integer> plantIds,
      LocalDateTime submission,
      int interval,
      LocalDateTime start,
      LocalDateTime end,
      String name,
      String requester) {
    if (!Set.of(15, 30, 60).contains(interval))
      throw new IllegalArgumentException("Use a 15, 30 or 60 minute interval.");
    if (name.isBlank() || name.length() > 200)
      throw new IllegalArgumentException("Enter a report name up to 200 characters.");
    if (start != null && end != null && !start.isBefore(end))
      throw new IllegalArgumentException("The end timestamp must be after the start.");
    var report = new ReportExecution();
    report.reportId = "RPT-" + UUID.randomUUID();
    report.name = name;
    report.dataType = dataType.equals("MMF") ? "MMF" : "DDQ";
    report.requestedBy = requester;
    report.reportType = requester.equals("System Scheduler") ? "Scheduled" : "On-Demand";
    report.submissionDate = submission;
    report.intervalMinutes = interval;
    report.dataSelection = Policies.canonical(dataType);
    report.dataRangeStart = start;
    report.dataRangeEnd = end;
    report.description =
        "plants=" + String.join(",", plantIds.stream().map(String::valueOf).toList());
    report.generationStatus = "Processing";
    db.insert(report);
    try {
      var export = exportData(report);
      var plants = export.plants();
      var prepared = export.prepared();
      report.plantCount = plants.size();
      report.recordCount = prepared.readings.size();
      report.anomalyCount = prepared.substitutions.size() + export.detected.size();
      Files.createDirectories(directory);
      write(report.reportId, "xlsx", workbook(report, plants, prepared));
      write(report.reportId, "csv", csv(report, plants, prepared));
      write(report.reportId, "anomalies.csv", anomalies(prepared, export.detected));
      report.mainFilePath = report.reportId + ".xlsx";
      report.anomalyFilePath = report.reportId + ".anomalies.csv";
      report.generationStatus = "Completed";
      db.update(report);
      accounts.audit(
          requester,
          "Report generated",
          "Report",
          report.reportId,
          "Success",
          report.recordCount + " reading(s); " + report.anomalyCount + " substitution(s)");
    } catch (Exception e) {
      report.generationStatus = "Failed";
      report.errorMessage = Objects.toString(e.getMessage(), "Report generation failed");
      db.update(report);
    }
    return report;
  }

  private ExportData exportData(ReportExecution report) {
    String type = report.dataType.equals("MMF") ? "MMF" : Policies.canonical(report.dataSelection);
    var selected =
        report.description != null && report.description.startsWith("plants=")
            ? ReadingService.plantIds(report.description.substring(7))
            : Set.<Integer>of();
    var plants =
        db.list(LssPlant.class, "ORDER BY \"PlantId\"").stream()
            .filter(
                p -> selected.isEmpty() ? Boolean.TRUE.equals(p.isActive) : selected.contains(p.id))
            .toList();
    if (plants.isEmpty())
      throw new IllegalArgumentException("Select at least one registered plant.");
    var ids = new LinkedHashSet<Integer>();
    plants.forEach(p -> ids.add(p.id));
    LocalDateTime start = report.dataRangeStart, end = report.dataRangeEnd;
    // Existing Blazor MMF reports recorded an inclusive range endpoint.
    if (report.reportId.startsWith("ODA-") && end != null) end = end.plusSeconds(1);
    var source =
        readings.select(type, ids, start == null ? report.submissionDate : null, start, end);
    if (source.isEmpty())
      throw new IllegalArgumentException(
          "No readings exist for the selected submission or data range.");
    var previous =
        readings.select(
            type,
            ids,
            start == null ? report.submissionDate.minusDays(1) : null,
            start == null ? null : start.minusDays(1),
            end == null ? null : end.minusDays(1));
    var prepared =
        prepare(
            type,
            report.submissionDate,
            report.intervalMinutes,
            plants,
            source,
            previous,
            validation.rules(),
            start,
            end);
    var jobIds = source.stream().map(r -> r.collectionJobId).distinct().toList();
    var detected = new ArrayList<DataAnomaly>();
    for (int i = 0; i < jobIds.size(); i += 500) {
      var group = jobIds.subList(i, Math.min(jobIds.size(), i + 500));
      detected.addAll(
          db.list(
              DataAnomaly.class,
              "WHERE \"CollectionJobId\" IN ("
                  + String.join(",", Collections.nCopies(group.size(), "?"))
                  + ")",
              group.toArray()));
    }
    if (type.equals("MMF")
        && (report.dataSelection.contains("All States")
            || report.dataSelection.contains("All Regions"))) {
      var aggregate =
          AnalyticsService.aggregate(
              plants,
              prepared.readings,
              report.dataSelection.contains("All States") ? "State" : "Region",
              true);
      plants = aggregate.plants();
      prepared = new Prepared(aggregate.readings(), prepared.substitutions);
    }
    return new ExportData(plants, prepared, detected);
  }

  public static Prepared prepare(
      String type,
      LocalDateTime submission,
      int interval,
      List<LssPlant> plants,
      List<SolarApiReading> source,
      List<SolarApiReading> previous,
      List<ValidationRule> rules,
      LocalDateTime rangeStart,
      LocalDateTime rangeEnd) {
    boolean mmf = type.equals("MMF");
    var current = new HashMap<String, SolarApiReading>();
    var fallback = new HashMap<String, SolarApiReading>();
    for (var r : source)
      current.put(
          r.plantId
              + "|"
              + (mmf ? r.parameter.toUpperCase(Locale.ROOT) : "Value")
              + "|"
              + r.intervalStart,
          r);
    for (var r : previous)
      fallback.put(
          r.plantId
              + "|"
              + (mmf ? r.parameter.toUpperCase(Locale.ROOT) : "Value")
              + "|"
              + (mmf ? r.intervalStart.toLocalTime() : r.intervalStart),
          r);
    LocalDateTime start =
        rangeStart != null
            ? rangeStart
            : mmf
                ? submission.toLocalDate().atStartOfDay()
                : source.stream()
                    .map(r -> r.intervalStart)
                    .min(LocalDateTime::compareTo)
                    .orElse(submission);
    LocalDateTime end =
        rangeEnd != null
            ? rangeEnd
            : mmf
                ? start.plusDays(1)
                : source.stream()
                    .map(r -> r.intervalStart)
                    .max(LocalDateTime::compareTo)
                    .orElse(start)
                    .plusMinutes(interval);
    if (Duration.between(start, end).toMinutes() / interval * Math.max(1, plants.size()) > 250000)
      throw new IllegalArgumentException("Report range is too large");
    var params = new TreeSet<String>();
    if (mmf) {
      source.forEach(r -> params.add(r.parameter));
      previous.forEach(r -> params.add(r.parameter));
    } else params.add("Value");
    var result = new ArrayList<SolarApiReading>();
    var substitutions = new ArrayList<Substitution>();
    for (var plant : plants)
      for (String parameter : params)
        for (LocalDateTime time = start; time.isBefore(end); time = time.plusMinutes(interval)) {
          String k = plant.id + "|" + (mmf ? parameter.toUpperCase(Locale.ROOT) : "Value") + "|";
          var original = current.get(k + time);
          String problem = ValidationService.problem(original, plant.capacityMw, rules);
          if (problem == null) {
            result.add(original);
            continue;
          }
          var before = fallback.get(k + (mmf ? time.toLocalTime() : time));
          BigDecimal value;
          String from;
          if (ValidationService.problem(before, plant.capacityMw, rules) == null) {
            value = before.value;
            from = "Previous-day submission; reading " + before.id;
          } else if (!mmf
              && original != null
              && plant.capacityMw.signum() > 0
              && original.value.compareTo(plant.capacityMw) > 0) {
            value = plant.capacityMw;
            from = "Registered plant capacity";
          } else {
            value = BigDecimal.ZERO;
            from = "Zero fallback";
          }
          var r = new SolarApiReading();
          r.plantId = plant.id;
          r.dataType = type;
          r.submissionDateTime = submission;
          r.intervalStart = time;
          r.intervalEnd = time.plusMinutes(interval);
          r.intervalMinutes = interval;
          r.parameter = parameter;
          r.unit = mmf ? Policies.MMF_UNITS.getOrDefault(parameter, "") : "MW";
          r.value = value;
          r.isSynthetic = true;
          r.syntheticReason = problem + "; " + from;
          result.add(r);
          substitutions.add(
              new Substitution(
                  plant.id,
                  parameter,
                  time,
                  original == null ? null : original.value,
                  value,
                  problem,
                  from));
        }
    return new Prepared(result, substitutions);
  }

  private static List<List<Object>> rows(
      ReportExecution report,
      List<LssPlant> plants,
      List<SolarApiReading> source,
      String parameter) {
    boolean mmf = report.dataType.equals("MMF");
    var selected = source.stream().filter(r -> !mmf || r.parameter.equals(parameter)).toList();
    var times = selected.stream().map(r -> r.intervalStart).distinct().sorted().toList();
    var index = new HashMap<String, BigDecimal>();
    selected.forEach(r -> index.put(r.plantId + "|" + r.intervalStart, r.value));
    var result = new ArrayList<List<Object>>();
    result.add(List.of("SOLAR DATA SUBMISSION / DATA MONITORING"));
    result.add(List.of("Data Type", report.dataSelection));
    result.add(List.of("Submission", report.submissionDate.toString()));
    result.add(List.of("Data Interval", report.intervalMinutes + " minutes"));
    if (mmf) {
      result.add(
          List.of("Parameter", parameter, "Unit", Policies.MMF_UNITS.getOrDefault(parameter, "")));
      var header = new ArrayList<Object>();
      header.add("LSS");
      times.forEach(t -> header.add(t.toString()));
      result.add(header);
      for (var plant : plants) {
        var row = new ArrayList<Object>();
        row.add(plant.plantId);
        for (var t : times) row.add(index.get(plant.id + "|" + t));
        result.add(row);
      }
    } else {
      var header =
          new ArrayList<Object>(
              List.of("Submission Date & Time", "Year", "Month", "Day", "Period"));
      plants.forEach(p -> header.add(p.plantId));
      result.add(header);
      for (var t : times) {
        var row =
            new ArrayList<Object>(
                List.of(
                    report.submissionDate.toString(),
                    t.getYear(),
                    t.getMonthValue(),
                    t.getDayOfMonth(),
                    (t.getHour() * 60 + t.getMinute()) / report.intervalMinutes + 1));
        for (var plant : plants) row.add(index.get(plant.id + "|" + t));
        result.add(row);
      }
    }
    return result;
  }

  private static List<String> parameters(ReportExecution report, Prepared prepared) {
    return report.dataType.equals("MMF")
        ? prepared.readings.stream().map(r -> r.parameter).distinct().sorted().toList()
        : List.of("DDQ");
  }

  private static byte[] workbook(ReportExecution report, List<LssPlant> plants, Prepared prepared)
      throws IOException {
    try (var book = new XSSFWorkbook();
        var output = new ByteArrayOutputStream()) {
      var title = book.createCellStyle();
      var font = book.createFont();
      font.setBold(true);
      font.setColor(IndexedColors.WHITE.getIndex());
      title.setFont(font);
      title.setFillForegroundColor(IndexedColors.DARK_TEAL.getIndex());
      title.setFillPattern(FillPatternType.SOLID_FOREGROUND);
      for (String parameter : parameters(report, prepared)) {
        var sheet =
            book.createSheet(
                parameter
                    .replaceAll("[\\\\/?*\\[\\]:]", "_")
                    .substring(0, Math.min(31, parameter.length())));
        var data = rows(report, plants, prepared.readings, parameter);
        for (int i = 0; i < data.size(); i++) {
          var row = sheet.createRow(i);
          for (int j = 0; j < data.get(i).size(); j++) {
            var cell = row.createCell(j);
            var value = data.get(i).get(j);
            if (value instanceof Number n) cell.setCellValue(n.doubleValue());
            else cell.setCellValue(Objects.toString(value, ""));
            if (i == 0) cell.setCellStyle(title);
          }
        }
        sheet.setColumnWidth(0, 28 * 256);
        for (int i = 1; i < Math.min(100, data.stream().mapToInt(List::size).max().orElse(1)); i++)
          sheet.setColumnWidth(i, 20 * 256);
        sheet.createFreezePane(
            report.dataType.equals("MMF") ? 1 : 5, report.dataType.equals("MMF") ? 6 : 5);
        sheet.getPrintSetup().setLandscape(true);
        sheet.setFitToPage(true);
      }
      book.write(output);
      return output.toByteArray();
    }
  }

  public static String safeCsv(String value) {
    String trimmed = value.stripLeading();
    return !trimmed.isEmpty()
            && (Set.of('=', '+', '@', '\t', '\r', '\n').contains(trimmed.charAt(0))
                || trimmed.charAt(0) == '-' && PayloadTranslator.number(trimmed) == null)
        ? "'" + value
        : value;
  }

  private static byte[] csv(ReportExecution report, List<LssPlant> plants, Prepared prepared)
      throws IOException {
    var text = new StringWriter();
    try (var printer = new CSVPrinter(text, CSVFormat.DEFAULT)) {
      for (String parameter : parameters(report, prepared)) {
        for (var row : rows(report, plants, prepared.readings, parameter))
          printer.printRecord(
              row.stream().map(v -> v instanceof String s ? safeCsv(s) : v).toList());
        printer.println();
      }
    }
    return text.toString().getBytes(StandardCharsets.UTF_8);
  }

  private static byte[] anomalies(Prepared prepared, List<DataAnomaly> detected)
      throws IOException {
    var text = new StringWriter();
    try (var printer = new CSVPrinter(text, CSVFormat.DEFAULT)) {
      printer.printRecord(
          "Plant",
          "Parameter",
          "Data Timestamp",
          "Original Value",
          "Report Value",
          "Anomaly",
          "Substitution Source");
      for (var s : prepared.substitutions)
        printer.printRecord(
            s.plantId,
            safeCsv(s.parameter),
            s.timestamp,
            s.original,
            s.value,
            safeCsv(s.reason),
            safeCsv(s.source));
      for (var a : detected)
        printer.printRecord(
            "Collection " + a.collectionJobId,
            safeCsv(a.fieldName),
            a.detectedAt,
            safeCsv(Objects.toString(a.originalValue, "")),
            "",
            safeCsv(a.reason),
            "Stored validation anomaly; " + a.status);
    }
    return text.toString().getBytes(StandardCharsets.UTF_8);
  }

  private Path path(String id, String suffix) {
    if (!id.matches("[A-Za-z0-9-]{1,60}"))
      throw new IllegalArgumentException("Invalid report identifier");
    return directory.resolve(id + "." + suffix);
  }

  private void write(String id, String suffix, byte[] bytes) throws IOException {
    var target = path(id, suffix);
    var temp = Files.createTempFile(directory, "report-", ".tmp");
    Files.write(temp, bytes);
    try {
      Files.move(temp, target, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
    } catch (AtomicMoveNotSupportedException e) {
      Files.move(temp, target, StandardCopyOption.REPLACE_EXISTING);
    }
  }

  public Download download(long id, String requester, String format) {
    var report =
        db.find(ReportExecution.class, id)
            .orElseThrow(() -> new IllegalArgumentException("Report not found"));
    if (!report.requestedBy.equalsIgnoreCase(requester))
      throw new AccessDeniedException("This report belongs to another user.");
    return attachment(report, format);
  }

  public Download attachment(ReportExecution report, String format) {
    if (!Set.of("xlsx", "csv", "anomalies.csv").contains(format)
        || !report.generationStatus.equals("Completed"))
      throw new IllegalArgumentException("Report download is unavailable");
    try {
      if (!Files.isRegularFile(path(report.reportId, format))) {
        var export = exportData(report);
        byte[] bytes =
            switch (format) {
              case "xlsx" -> workbook(report, export.plants, export.prepared);
              case "csv" -> csv(report, export.plants, export.prepared);
              default -> anomalies(export.prepared, export.detected);
            };
        return new Download(
            bytes,
            report.reportId + "." + format,
            format.equals("xlsx")
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv;charset=UTF-8");
      }
      return new Download(
          Files.readAllBytes(path(report.reportId, format)),
          report.reportId + "." + format,
          format.equals("xlsx")
              ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              : "text/csv;charset=UTF-8");
    } catch (IOException e) {
      throw new IllegalArgumentException(
          "The report file is unavailable. Generate the report again.");
    }
  }
}
