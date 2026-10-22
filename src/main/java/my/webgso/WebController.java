package my.webgso;

import jakarta.servlet.http.HttpServletRequest;
import java.lang.reflect.*;
import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.http.*;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.ui.Model;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class WebController {
  private final Store db;
  private final Accounts accounts;
  private final AdminService admin;
  private final CollectionService collection;
  private final ReadingService readings;
  private final ReportService reports;
  private final ManualBatchService batches;

  public WebController(
      Store db,
      Accounts accounts,
      AdminService admin,
      CollectionService collection,
      ReadingService readings,
      ReportService reports,
      ManualBatchService batches) {
    this.db = db;
    this.accounts = accounts;
    this.admin = admin;
    this.collection = collection;
    this.readings = readings;
    this.reports = reports;
    this.batches = batches;
  }

  static Accounts.Principal principal(Authentication auth) {
    return (Accounts.Principal) auth.getPrincipal();
  }

  static String requester(Authentication auth) {
    return "user:" + principal(auth).id();
  }

  @ModelAttribute
  public void common(Model model, Authentication auth) {
    model.addAttribute(
        "navigation",
        Resources.ALL.stream()
            .filter(
                r ->
                    !Set.of(
                            "/admin/api-configurations",
                            "/admin/report-recipients",
                            "/admin/anomalies",
                            "/admin/delivery-monitor")
                        .contains(r.path()))
            .toList());
    model.addAttribute(
        "isAdmin",
        auth != null
            && auth.getAuthorities().stream()
                .anyMatch(
                    a ->
                        Set.of("ROLE_Administrator", "ROLE_SuperAdministrator")
                            .contains(a.getAuthority())));
    if (auth != null && auth.getPrincipal() instanceof Accounts.Principal p) {
      model.addAttribute("account", p);
      model.addAttribute(
          "notifications",
          db.list(
              UserAccessNotification.class,
              "WHERE \"UserId\"=? ORDER BY \"CreatedAt\" DESC LIMIT 15",
              p.id()));
    }
    model.addAttribute("title", "LSS Repository");
    model.addAttribute("today", LocalDate.now(Policies.LOCAL));
  }

  @GetMapping("/login")
  public String login(Model model) {
    model.addAttribute("title", "Sign in");
    return "login";
  }

  @GetMapping("/register")
  public String registration(Model model) {
    model.addAttribute("title", "Create an account");
    return "register";
  }

  @PostMapping("/account/register")
  public String register(
      @RequestParam String username,
      @RequestParam String email,
      @RequestParam String displayName,
      @RequestParam String password,
      @RequestParam String confirmPassword) {
    if (!password.equals(confirmPassword))
      throw new IllegalArgumentException("The passwords do not match.");
    accounts.register(username.trim(), email.trim(), displayName.trim(), password);
    return "redirect:/login?registered=pending";
  }

  @GetMapping("/")
  public String dashboard(Model model) {
    model.addAttribute("title", "Overview");
    model.addAttribute("plantCount", db.count(LssPlant.class));
    model.addAttribute("readingCount", db.count(SolarApiReading.class));
    model.addAttribute("collectionCount", db.count(CollectionJobEntity.class));
    model.addAttribute(
        "anomalyCount",
        db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"DataAnomalies\" WHERE \"Status\"='Open'", Long.class));
    model.addAttribute("plants", db.list(LssPlant.class, "ORDER BY \"PlantId\" LIMIT 12"));
    model.addAttribute(
        "jobs", db.list(CollectionJobEntity.class, "ORDER BY \"StartedAt\" DESC LIMIT 10"));
    model.addAttribute(
        "recentReports", db.list(ReportExecution.class, "ORDER BY \"RequestedAt\" DESC LIMIT 5"));
    return "dashboard";
  }

  @GetMapping({"/ddq-analytics", "/mmf-analytics"})
  public String analytics(
      HttpServletRequest request,
      Model model,
      @RequestParam(defaultValue = "") String plantIds,
      @RequestParam(defaultValue = "Rolling 24 Hour") String dataType,
      @RequestParam(defaultValue = "") String from,
      @RequestParam(defaultValue = "") String to,
      @RequestParam(defaultValue = "") String submission,
      @RequestParam(defaultValue = "") String parameter,
      @RequestParam(defaultValue = "") String state,
      @RequestParam(defaultValue = "") String region,
      @RequestParam(defaultValue = "Plant") String scope,
      @RequestParam(defaultValue = "0") int page) {
    boolean mmf = request.getRequestURI().equals("/mmf-analytics");
    String type = mmf ? "MMF" : Policies.canonical(dataType);
    LocalDateTime
        start = from.isBlank() ? LocalDate.now(Policies.LOCAL).atStartOfDay() : parseTime(from),
        end = to.isBlank() ? start.plusDays(1) : parseTime(to);
    if (!start.isBefore(end)) throw new IllegalArgumentException("End must be after start");
    var registered = db.list(LssPlant.class, "WHERE \"IsActive\"=true ORDER BY \"PlantId\"");
    var selectedIds = ReadingService.plantIds(plantIds);
    var selectedPlants =
        registered.stream()
            .filter(
                p ->
                    (selectedIds.isEmpty() || selectedIds.contains(p.id))
                        && (state.isBlank() || p.state.equals(state))
                        && (region.isBlank() || AnalyticsService.region(p.state).equals(region)))
            .toList();
    var filteredIds = new LinkedHashSet<Integer>();
    selectedPlants.forEach(p -> filteredIds.add(p.id));
    if (filteredIds.isEmpty()) filteredIds.add(-1);
    var source =
        readings.select(
            type, filteredIds, submission.isBlank() ? null : parseTime(submission), start, end);
    String activeParameter = mmf ? (parameter.isBlank() ? "AmbientTemp" : parameter) : parameter;
    if (!activeParameter.isBlank())
      source = source.stream().filter(r -> r.parameter.equalsIgnoreCase(activeParameter)).toList();
    var grouped = AnalyticsService.aggregate(selectedPlants, source, scope, mmf);
    source = grouped.readings();
    var buckets = new TreeMap<LocalDateTime, List<BigDecimal>>();
    for (var r : source)
      buckets.computeIfAbsent(r.intervalStart, k -> new ArrayList<>()).add(r.value);
    var values = new ArrayList<Double>();
    for (var bucket : buckets.values())
      values.add(
          mmf
              ? Policies.average(bucket).doubleValue()
              : bucket.stream().reduce(BigDecimal.ZERO, BigDecimal::add).doubleValue());
    double max = values.stream().mapToDouble(Double::doubleValue).max().orElse(1),
        min = Math.min(0, values.stream().mapToDouble(Double::doubleValue).min().orElse(0));
    if (max == min) max = min + 1;
    var points = new ArrayList<String>();
    for (int i = 0; i < values.size(); i++)
      points.add(
          String.format(
              Locale.ROOT,
              "%.2f,%.2f",
              40 + i * 920.0 / Math.max(1, values.size() - 1),
              230 - (values.get(i) - min) * 190 / (max - min)));
    int safePage = Math.clamp(page, 0, Math.max(0, (source.size() - 1) / 25));
    model.addAttribute("title", mmf ? "MMF Analytics" : "DDQ Analytics");
    model.addAttribute("mmf", mmf);
    model.addAttribute("plants", registered);
    var plantNames = new HashMap<Integer, String>();
    grouped.plants().forEach(p -> plantNames.put(p.id, p.plantId));
    model.addAttribute("plantNames", plantNames);
    model.addAttribute(
        "states", registered.stream().map(p -> p.state).distinct().sorted().toList());
    model.addAttribute(
        "regions",
        registered.stream()
            .map(p -> AnalyticsService.region(p.state))
            .distinct()
            .sorted()
            .toList());
    model.addAttribute("state", state);
    model.addAttribute("region", region);
    model.addAttribute("scope", scope);
    model.addAttribute("plantIds", plantIds);
    model.addAttribute("dataType", type);
    model.addAttribute("dataTypes", Policies.DDQ_TYPES);
    model.addAttribute("parameters", Policies.MMF_UNITS.keySet().stream().sorted().toList());
    model.addAttribute("parameter", activeParameter);
    model.addAttribute("from", start);
    model.addAttribute("to", end);
    model.addAttribute("submission", submission);
    model.addAttribute("points", String.join(" ", points));
    model.addAttribute("chartMax", String.format(Locale.ROOT, "%.2f", max));
    model.addAttribute("chartMin", String.format(Locale.ROOT, "%.2f", min));
    model.addAttribute("total", source.size());
    model.addAttribute(
        "rows", source.subList(safePage * 25, Math.min(source.size(), safePage * 25 + 25)));
    model.addAttribute("page", safePage);
    return "analytics";
  }

  public static LocalDateTime parseTime(String value) {
    var result = PayloadTranslator.timestamp(value);
    if (result == null) throw new IllegalArgumentException("Enter a valid date and time");
    return result;
  }

  @GetMapping("/collection-management")
  public String collections(
      Model model,
      @RequestParam(defaultValue = "0") int page,
      @RequestParam(defaultValue = "") String batch,
      Authentication auth) {
    model.addAttribute("batch", batch.isBlank() ? null : batches.get(batch, requester(auth)));
    model.addAttribute("title", "Collection Management");
    model.addAttribute("plants", db.list(LssPlant.class, "ORDER BY \"PlantId\""));
    model.addAttribute(
        "dataTypes", List.of("Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead", "MMF"));
    model.addAttribute(
        "jobs",
        db.list(
            CollectionJobEntity.class,
            "ORDER BY \"StartedAt\" DESC LIMIT 25 OFFSET ?",
            Math.clamp(page, 0, 100000) * 25));
    model.addAttribute("page", Math.max(0, page));
    model.addAttribute("total", db.count(CollectionJobEntity.class));
    model.addAttribute("now", LocalDateTime.now(Policies.LOCAL).withSecond(0).withNano(0));
    return "collections";
  }

  @PostMapping("/collections/run")
  public String collect(
      @RequestParam int plantId,
      @RequestParam String dataType,
      @RequestParam String submission,
      @RequestParam(defaultValue = "false") boolean replace,
      Authentication auth,
      RedirectAttributes flash) {
    var job =
        collection.collect(
            plantId,
            dataType,
            parseTime(submission),
            replace,
            principal(auth).username(),
            null,
            null,
            0);
    flash.addFlashAttribute(
        "message",
        job.status + ": " + (job.errorMessage == null ? job.responseMessage : job.errorMessage));
    return "redirect:/collection-management";
  }

  @PostMapping("/collections/batch")
  public String batch(
      @RequestParam String dataType,
      @RequestParam(defaultValue = "") String plantIds,
      @RequestParam String submission,
      @RequestParam(defaultValue = "false") boolean replace,
      Authentication auth) {
    var batch =
        batches.start(
            ReadingService.plantIds(plantIds),
            dataType,
            parseTime(submission),
            replace,
            requester(auth),
            principal(auth).username());
    return "redirect:/collection-management?batch=" + batch.id;
  }

  @GetMapping("/collections/{id}")
  public String collection(@PathVariable long id, Model model) {
    var job = db.find(CollectionJobEntity.class, id).orElseThrow();
    model.addAttribute("title", "Collection " + job.collectionId);
    model.addAttribute("job", job);
    model.addAttribute(
        "anomalies",
        db.list(DataAnomaly.class, "WHERE \"CollectionJobId\"=? ORDER BY \"Id\" LIMIT 500", id));
    model.addAttribute(
        "raw",
        db.list(
            RawApiResponse.class, "WHERE \"CollectionJobId\"=? ORDER BY \"Id\" DESC LIMIT 1", id));
    return "collection";
  }

  @GetMapping("/on-demand-analysis")
  public String reports(Model model, Authentication auth) {
    model.addAttribute("title", "On-Demand Analysis");
    model.addAttribute("plants", db.list(LssPlant.class, "ORDER BY \"PlantId\""));
    model.addAttribute(
        "reports",
        db.list(
            ReportExecution.class,
            "WHERE \"RequestedBy\"=? ORDER BY \"RequestedAt\" DESC LIMIT 100",
            requester(auth)));
    model.addAttribute(
        "dataTypes", List.of("Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead", "MMF"));
    return "reports";
  }

  @PostMapping("/reports/generate")
  public String generate(
      @RequestParam String dataType,
      @RequestParam(defaultValue = "") String plantIds,
      @RequestParam String submission,
      @RequestParam(defaultValue = "15") int interval,
      @RequestParam(defaultValue = "") String from,
      @RequestParam(defaultValue = "") String to,
      @RequestParam String name,
      Authentication auth,
      RedirectAttributes flash) {
    var report =
        reports.generate(
            dataType,
            ReadingService.plantIds(plantIds),
            parseTime(submission),
            interval,
            from.isBlank() ? null : parseTime(from),
            to.isBlank() ? null : parseTime(to),
            name,
            requester(auth));
    flash.addFlashAttribute(
        "message",
        report.generationStatus.equals("Completed")
            ? "Report ready: "
                + report.recordCount
                + " readings; "
                + report.anomalyCount
                + " substitutions."
            : report.errorMessage);
    return "redirect:/on-demand-analysis";
  }

  @GetMapping("/reports/{id}/download")
  public ResponseEntity<byte[]> download(
      @PathVariable long id,
      @RequestParam(defaultValue = "xlsx") String format,
      Authentication auth) {
    var file = reports.download(id, requester(auth), format);
    return ResponseEntity.ok()
        .header(
            HttpHeaders.CONTENT_DISPOSITION,
            ContentDisposition.attachment().filename(file.filename()).build().toString())
        .contentType(MediaType.parseMediaType(file.contentType()))
        .body(file.data());
  }

  @GetMapping({
    "/admin/lss-plants",
    "/admin/api-configurations",
    "/admin/collection-schedules",
    "/admin/report-automation",
    "/admin/report-recipients",
    "/admin/data-quality",
    "/admin/anomalies",
    "/admin/audit-logs",
    "/admin/delivery-monitor"
  })
  public String resource(
      HttpServletRequest request,
      Model model,
      @RequestParam(defaultValue = "0") int page,
      @RequestParam(defaultValue = "") String q) {
    var resource = Resources.get(request.getRequestURI());
    int safePage = Math.clamp(page, 0, 100000);
    String predicate = "";
    var args = new ArrayList<Object>();
    if (!q.isBlank()) {
      if (q.length() > 100) throw new IllegalArgumentException("Search text is too long");
      predicate =
          " WHERE "
              + String.join(
                  " OR ",
                  resource.columns().stream()
                      .map(
                          n -> {
                            try {
                              return "LOWER(CAST("
                                  + Store.q(Store.column(resource.type().getField(n)))
                                  + " AS VARCHAR)) LIKE ?";
                            } catch (Exception e) {
                              throw new IllegalStateException(e);
                            }
                          })
                      .toList());
      for (String c : resource.columns()) args.add("%" + q.toLowerCase(Locale.ROOT) + "%");
    }
    long total =
        db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM " + Store.table(resource.type()) + predicate,
                Long.class,
                args.toArray());
    args.add(safePage * 25);
    var records =
        db.list(
            resource.type(), predicate + " ORDER BY \"Id\" DESC LIMIT 25 OFFSET ?", args.toArray());
    var table = new ArrayList<Map<String, Object>>();
    for (Object record : records) {
      var row = AdminService.values(record, resource.columns());
      try {
        row.put("id", record.getClass().getField("id").get(record));
      } catch (Exception e) {
        throw new IllegalStateException(e);
      }
      table.add(row);
    }
    model.addAttribute("title", resource.title());
    model.addAttribute("resource", resource);
    model.addAttribute(
        "columns",
        resource.columns().stream()
            .map(n -> Map.of("name", n, "label", Resources.label(n)))
            .toList());
    model.addAttribute("rows", table);
    model.addAttribute("q", q);
    model.addAttribute("page", safePage);
    model.addAttribute("total", total);
    return "resource";
  }

  @GetMapping({
    "/admin/lss-plants/edit",
    "/admin/api-configurations/edit",
    "/admin/collection-schedules/edit",
    "/admin/report-automation/edit",
    "/admin/report-recipients/edit",
    "/admin/data-quality/edit",
    "/admin/anomalies/edit"
  })
  public String edit(
      HttpServletRequest request, Model model, @RequestParam(required = false) Long id)
      throws ReflectiveOperationException {
    var resource = Resources.get(request.getRequestURI().replaceFirst("/edit$", ""));
    if (resource.type() == DataAnomaly.class && id == null)
      throw new IllegalArgumentException("Select an existing anomaly");
    Object record =
        id == null
            ? resource.type().getConstructor().newInstance()
            : db.find(resource.type(), id)
                .orElseThrow(() -> new IllegalArgumentException("Record not found"));
    var fields = new ArrayList<Map<String, Object>>();
    for (String name :
        (record instanceof ValidationRule r && r.ruleSource.equals("Fixed")
            ? List.of("isActive")
            : resource.editable())) {
      var f = resource.type().getField(name);
      var field = new LinkedHashMap<String, Object>();
      field.put("name", name);
      field.put("label", Resources.label(name));
      field.put("value", Objects.toString(f.get(record), ""));
      field.put(
          "type",
          f.getType() == Boolean.class
              ? "checkbox"
              : f.getType() == String.class ? "text" : "number");
      field.put("checked", Boolean.TRUE.equals(f.get(record)));
      field.put(
          "options",
          name.equals("dataType") && resource.type() == ValidationRule.class
              ? List.of("DDQ", "MMF")
              : options(name));
      fields.add(field);
    }
    model.addAttribute("title", (id == null ? "Add " : "Edit ") + resource.title());
    model.addAttribute("resource", resource);
    model.addAttribute("fields", fields);
    model.addAttribute("id", id);
    return "edit";
  }

  private static List<String> options(String name) {
    return switch (name) {
      case "apiType" -> List.of("DDQ", "MMF");
      case "dataType" ->
          List.of(
              "DDQ",
              "Rolling 24 Hour",
              "Day Ahead",
              "Week Ahead",
              "4 Month Ahead",
              "MMF",
              "Daily",
              "Weekly",
              "4 Month");
      case "interval" -> List.of("15 minutes", "30 minutes", "1 hour");
      case "scheduleType" -> List.of("Daily", "Weekly", "4 Month", "Rolling 24 Hour");
      case "severity" -> List.of("Error", "Warning");
      case "status" -> List.of("Open", "Resolved", "Ignored");
      default -> List.of();
    };
  }

  @PostMapping({
    "/admin/lss-plants/save",
    "/admin/api-configurations/save",
    "/admin/collection-schedules/save",
    "/admin/report-automation/save",
    "/admin/report-recipients/save",
    "/admin/data-quality/save",
    "/admin/anomalies/save"
  })
  public String save(
      HttpServletRequest request,
      @RequestParam Map<String, String> form,
      Authentication auth,
      RedirectAttributes flash) {
    var resource = Resources.get(request.getRequestURI().replaceFirst("/save$", ""));
    admin.save(resource, form, principal(auth).username());
    flash.addFlashAttribute("message", "Configuration saved.");
    return "redirect:" + resource.path();
  }

  @PostMapping("/admin/plants/{id}/test")
  public String testPlant(
      @PathVariable int id,
      @RequestParam String dataType,
      Authentication auth,
      RedirectAttributes flash) {
    collection.test(id, dataType, principal(auth).username());
    flash.addFlashAttribute("message", "The API returned readable measurements.");
    return "redirect:/admin/lss-plants";
  }

  @GetMapping("/admin/users")
  public String users(Model model) {
    model.addAttribute("title", "User Management");
    model.addAttribute(
        "users", db.list(ApplicationUser.class, "ORDER BY \"AccessRequestedAt\" DESC LIMIT 500"));
    var roles = new HashMap<String, String>();
    for (var u : db.list(ApplicationUser.class, "LIMIT 500"))
      roles.put(u.id, String.join(", ", accounts.roles(u.id)));
    model.addAttribute("roles", roles);
    return "users";
  }

  @PostMapping("/admin/users/{id}/review")
  public String review(
      @PathVariable String id,
      @RequestParam String decision,
      @RequestParam String role,
      @RequestParam(defaultValue = "") String note,
      Authentication auth,
      RedirectAttributes flash) {
    if (note.length() > 1000) throw new IllegalArgumentException("Review note is too long");
    accounts.review(auth, id, decision, role, note);
    flash.addFlashAttribute("message", "Account access updated.");
    return "redirect:/admin/users";
  }

  @PostMapping("/notifications/{id}/read")
  public String read(@PathVariable long id, Authentication auth) {
    db.jdbc()
        .update(
            "UPDATE \"UserAccessNotifications\" SET \"ReadAt\"=? WHERE \"Id\"=? AND \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC),
            id,
            principal(auth).id());
    return "redirect:/";
  }
}
