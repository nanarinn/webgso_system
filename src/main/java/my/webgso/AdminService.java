package my.webgso;

import java.lang.reflect.*;
import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class AdminService {
  public static final class ApiTestsFailed extends IllegalArgumentException {
    public final Map<String, String> errors;

    public ApiTestsFailed(Map<String, String> errors) {
      super("One or more API tests failed. No changes were saved.");
      this.errors = Map.copyOf(errors);
    }
  }

  private final Store db;
  private final Accounts accounts;
  private final PlantApiClient client;
  private final PlantApiProbe probe;

  public AdminService(Store db, Accounts accounts, PlantApiClient client, PlantApiProbe probe) {
    this.db = db;
    this.accounts = accounts;
    this.client = client;
    this.probe = probe;
  }

  @Transactional
  public int savePlantApi(int plantId, String field, String url, String actor) {
    plant(plantId);
    var slot =
        PlantApiSlots.ALL.stream()
            .filter(item -> item.field().equals(field))
            .findFirst()
            .orElseThrow(() -> new IllegalArgumentException("Choose a valid API dataset"));
    String link = url.trim();
    if (link.length() > 1000) throw new IllegalArgumentException("API URL is too long");
    int count = link.isBlank() ? 0 : probe.verify(link, slot.apiType(), slot.dataType(), plantId);
    storePlantApi(plantId, slot, link);
    accounts.audit(
        actor,
        link.isBlank() ? "API removed" : "API tested and saved",
        "Plant API",
        Integer.toString(plantId),
        "Success",
        slot.label());
    return count;
  }

  private void storePlantApi(int plantId, PlantApiSlots.Slot slot, String link) {
    var existing =
        db.list(
            PlantApiConfiguration.class,
            "WHERE \"PlantId\"=? AND \"ApiType\"=? AND \"DataType\"=?",
            plantId,
            slot.apiType(),
            slot.dataType());
    if (link.isBlank()) {
      db.jdbc()
          .update(
              "DELETE FROM \"PlantApiConfigurations\" WHERE \"PlantId\"=? AND \"ApiType\"=? AND"
                  + " \"DataType\"=?",
              plantId,
              slot.apiType(),
              slot.dataType());
    } else {
      var api = existing.isEmpty() ? new PlantApiConfiguration() : existing.getFirst();
      api.plantId = plantId;
      api.apiType = slot.apiType();
      api.dataType = slot.dataType();
      api.apiLink = link;
      if (existing.isEmpty()) db.insert(api);
      else db.update(api);
    }
  }

  @Transactional
  public LssPlant savePlant(Map<String, String> form, String actor) {
    String id = form.getOrDefault("id", "");
    int plantId = id.isBlank() ? 0 : Integer.parseInt(id);
    if (plantId != 0) plant(plantId);
    // Check every changed endpoint so each failed card can show its own result.
    var errors = new LinkedHashMap<String, String>();
    for (var slot : PlantApiSlots.ALL) {
      if (!form.containsKey(slot.field())) continue;
      String link = form.get(slot.field()).trim();
      if (link.length() > 1000) {
        errors.put(slot.field(), "API test failed. API not saved. URL is too long.");
        continue;
      }
      var existing =
          db.list(
              PlantApiConfiguration.class,
              "WHERE \"PlantId\"=? AND \"ApiType\"=? AND \"DataType\"=?",
              plantId,
              slot.apiType(),
              slot.dataType());
      if (!link.isBlank() && (existing.isEmpty() || !existing.getFirst().apiLink.equals(link))) {
        try {
          probe.verify(link, slot.apiType(), slot.dataType(), plantId);
        } catch (IllegalArgumentException e) {
          errors.put(slot.field(), "API test failed. API not saved. " + e.getMessage());
        }
      }
    }
    if (!errors.isEmpty()) throw new ApiTestsFailed(errors);
    var plant = (LssPlant) save(Resources.get("/admin/lss-plants"), form, actor);
    for (var slot : PlantApiSlots.ALL) {
      // Old clients do not send these fields: leave their existing API links alone.
      if (!form.containsKey(slot.field())) continue;
      String link = form.get(slot.field()).trim();
      storePlantApi(plant.id, slot, link);
    }
    return plant;
  }

  @Transactional
  public Object save(Resources.Definition resource, Map<String, String> form, String actor) {
    try {
      String rawId = form.getOrDefault("id", "");
      Object record =
          rawId.isBlank()
              ? resource.type().getConstructor().newInstance()
              : db.find(resource.type(), Long.parseLong(rawId))
                  .orElseThrow(() -> new IllegalArgumentException("Record not found"));
      List<String> editable =
          record instanceof ValidationRule rule && rule.ruleSource.equals("Fixed")
              ? List.of("isActive")
              : resource.editable();
      for (String name : editable) {
        Field field = resource.type().getField(name);
        String input = form.getOrDefault(name, "").trim();
        Object value;
        if (field.getType() == Boolean.class)
          value = form.containsKey(name) && Set.of("true", "on").contains(input);
        else if (field.getType() == String.class) {
          int maximum =
              switch (name) {
                case "apiLink", "ddqApiEndpoint", "mmfApiEndpoint" -> 1000;
                case "description" -> 1000;
                case "scheduleCode", "code" -> 40;
                case "state" -> 60;
                case "plantId" -> 20;
                case "name" -> 160;
                case "email" -> 320;
                case "fieldName" -> 100;
                default -> 100;
              };
          if (input.length() > maximum)
            throw new IllegalArgumentException(Resources.label(name) + " is too long");
          value = input;
        } else if (input.isEmpty() && field.getType() == BigDecimal.class) value = null;
        else if (field.getType() == Integer.class) value = Integer.valueOf(input);
        else if (field.getType() == Long.class) value = Long.valueOf(input);
        else if (field.getType() == BigDecimal.class) value = new BigDecimal(input);
        else throw new IllegalArgumentException("Unsupported form field");
        field.set(record, value);
      }
      validate(record, rawId.isBlank(), actor);
      if (rawId.isBlank()) db.insert(record);
      else db.update(record);
      accounts.audit(
          actor,
          rawId.isBlank() ? "Created" : "Updated",
          resource.title(),
          Objects.toString(resource.type().getField("id").get(record)),
          "Success",
          "Saved repository configuration");
      return record;
    } catch (NumberFormatException e) {
      throw new IllegalArgumentException("Enter valid numbers in numeric fields.");
    } catch (ReflectiveOperationException e) {
      throw new IllegalStateException(e);
    }
  }

  private static void nonblank(String value, String field) {
    if (value == null || value.isBlank())
      throw new IllegalArgumentException(field + " is required");
  }

  private void plant(Integer id) {
    if (db.find(LssPlant.class, id).isEmpty())
      throw new IllegalArgumentException("Select a registered plant");
  }

  private void dataset(String apiType, String dataType) {
    if (!Set.of("DDQ", "MMF").contains(apiType)
        || !((apiType.equals("MMF") && dataType.equals("MMF"))
            || (apiType.equals("DDQ")
                && Policies.DDQ_TYPES.contains(Policies.canonical(dataType))
                && !dataType.equals("MMF"))))
      throw new IllegalArgumentException("Choose a valid API and dataset");
  }

  private void validate(Object record, boolean create, String actor) {
    if (record instanceof LssPlant p) {
      nonblank(p.plantId, "Plant ID");
      nonblank(p.name, "Plant name");
      if (p.capacityMw == null || p.capacityMw.signum() < 0)
        throw new IllegalArgumentException("Capacity cannot be negative");
      if (!p.ddqApiEndpoint.isBlank()) client.validate(p.ddqApiEndpoint);
      if (!p.mmfApiEndpoint.isBlank()) client.validate(p.mmfApiEndpoint);
      var original = create ? new LssPlant() : db.find(LssPlant.class, p.id).orElseThrow();
      if ((create || !p.plantId.equals(original.plantId)) && !p.plantId.matches("[0-9]{1,20}"))
        throw new IllegalArgumentException(
            "Plant ID must contain numbers only, for example 01 or 02.");
      if (!p.ddqApiEndpoint.isBlank() && !p.ddqApiEndpoint.equals(original.ddqApiEndpoint))
        probe.verify(p.ddqApiEndpoint, "DDQ", "Rolling 24 Hour", p.id);
      if (!p.mmfApiEndpoint.isBlank() && !p.mmfApiEndpoint.equals(original.mmfApiEndpoint))
        probe.verify(p.mmfApiEndpoint, "MMF", "MMF", p.id);
    }
    if (record instanceof PlantApiConfiguration p) {
      plant(p.plantId);
      dataset(p.apiType, p.dataType);
      p.dataType = p.apiType.equals("MMF") ? "MMF" : Policies.canonical(p.dataType);
      client.validate(p.apiLink);
      var conflicts =
          db.list(
              PlantApiConfiguration.class,
              "WHERE \"PlantId\"=? AND \"ApiType\"=? AND \"DataType\"=? AND \"Id\"<>?",
              p.plantId,
              p.apiType,
              p.dataType,
              p.id);
      if (!conflicts.isEmpty())
        throw new IllegalArgumentException("A link already exists for this plant and dataset");
      probe.verify(p.apiLink, p.apiType, p.dataType, p.plantId);
    }
    if (record instanceof CollectionSchedule s) {
      plant(s.plantId);
      nonblank(s.scheduleCode, "Schedule code");
      nonblank(s.name, "Name");
      dataset(s.apiType, s.dataType);
      LocalTime.parse(s.runTime);
      ZoneId.of(s.timeZoneId);
      if (s.retryCount < 0
          || s.retryCount > 5
          || !Set.of("15 minutes", "30 minutes", "1 hour").contains(s.interval))
        throw new IllegalArgumentException("Use 0–5 retries and a valid interval");
      if (!s.cronExpression.isBlank())
        org.springframework.scheduling.support.CronExpression.parse(s.cronExpression);
      validateDay(s.dataType, s.scheduleDay, s.scheduleMonths);
    }
    if (record instanceof ReportScheduleEntity s) {
      nonblank(s.name, "Name");
      nonblank(s.scheduleCode, "Schedule code");
      dataset(s.apiType, s.dataType);
      LocalTime.parse(s.executionTime);
      if (!Set.of("Daily", "Weekly", "4 Month", "Rolling 24 Hour").contains(s.scheduleType)
          || !Set.of(15, 30, 60).contains(s.intervalMinutes))
        throw new IllegalArgumentException("Choose a valid report schedule and interval");
      validateDay(s.scheduleType, s.scheduleDay, s.scheduleMonths);
    }
    if (record instanceof ReportScheduleRecipient r) {
      if (db.find(ReportScheduleEntity.class, r.reportScheduleId).isEmpty())
        throw new IllegalArgumentException("Select an existing report schedule");
      try {
        new jakarta.mail.internet.InternetAddress(r.email, true).validate();
      } catch (Exception e) {
        throw new IllegalArgumentException("Enter a valid email address");
      }
    }
    if (record instanceof ValidationRule r) {
      nonblank(r.code, "Rule code");
      nonblank(r.name, "Name");
      if (!Set.of("DDQ", "MMF").contains(r.dataType)
          || !Set.of("Error", "Warning").contains(r.severity))
        throw new IllegalArgumentException("Choose a valid rule type and severity");
      if (r.minimumValue != null
          && r.maximumValue != null
          && r.minimumValue.compareTo(r.maximumValue) > 0)
        throw new IllegalArgumentException("Minimum exceeds maximum");
      if (r.changeThreshold != null && r.changeThreshold.signum() < 0)
        throw new IllegalArgumentException("Change threshold cannot be negative");
      if (create) {
        r.ruleSource = "User";
        r.createdBy = actor;
      } else r.updatedAt = OffsetDateTime.now(ZoneOffset.UTC);
    }
    if (record instanceof DataAnomaly a) {
      if (!Set.of("Open", "Resolved", "Ignored").contains(a.status))
        throw new IllegalArgumentException("Choose Open, Resolved or Ignored");
    }
  }

  private static void validateDay(String type, String day, String months) {
    if (Set.of("Weekly", "Week Ahead").contains(type))
      try {
        DayOfWeek.valueOf(day.toUpperCase(Locale.ROOT));
      } catch (Exception e) {
        throw new IllegalArgumentException("Enter a weekday, for example Monday");
      }
    if (type.equals("4 Month") || type.startsWith("4 Month Ahead")) {
      int n;
      try {
        n = Integer.parseInt(day);
      } catch (Exception e) {
        throw new IllegalArgumentException("Enter a day of month");
      }
      if (n < 1
          || n > 31
          || Policies.months(months).isEmpty()
          || Policies.months(months).stream().anyMatch(m -> m < 1 || m > 12))
        throw new IllegalArgumentException("Choose a day from 1–31 and month numbers from 1–12");
    }
  }

  public static Map<String, Object> values(Object record, List<String> columns) {
    var result = new LinkedHashMap<String, Object>();
    for (String column : columns)
      try {
        result.put(column, record.getClass().getField(column).get(record));
      } catch (Exception e) {
        throw new IllegalStateException(e);
      }
    return result;
  }
}
