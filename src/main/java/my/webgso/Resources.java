package my.webgso;

import java.util.*;
import my.webgso.Models.*;

/** Explicit form allowlists prevent binding IDs, audit fields or credentials from browser input. */
public final class Resources {
  public record Definition(
      String path, String title, Class<?> type, List<String> editable, List<String> columns) {}

  private static Definition def(
      String path, String title, Class<?> type, String editable, String columns) {
    return new Definition(
        path,
        title,
        type,
        editable.isBlank() ? List.of() : List.of(editable.split(",")),
        List.of(columns.split(",")));
  }

  public static final List<Definition> ALL =
      List.of(
          def(
              "/admin/lss-plants",
              "LSS Plant Config",
              LssPlant.class,
              "plantId,name,state,capacityMw,ddqApiEndpoint,mmfApiEndpoint,isActive",
              "plantId,name,state,capacityMw,apiConnectionStatus,isActive"),
          def(
              "/admin/api-configurations",
              "Plant API Links",
              PlantApiConfiguration.class,
              "plantId,apiType,dataType,apiLink",
              "plantId,apiType,dataType,apiLink"),
          def(
              "/admin/collection-schedules",
              "Collection Schedules",
              CollectionSchedule.class,
              "plantId,scheduleCode,name,apiType,dataType,interval,runTime,scheduleDay,scheduleMonths,cronExpression,timeZoneId,retryCount,isActive",
              "scheduleCode,name,plantId,apiType,dataType,runTime,interval,isActive"),
          def(
              "/admin/report-automation",
              "Report Automation",
              ReportScheduleEntity.class,
              "scheduleCode,name,apiType,dataType,intervalMinutes,scheduleType,scheduleDay,scheduleMonths,executionTime,isActive",
              "scheduleCode,name,apiType,dataType,scheduleType,executionTime,isActive"),
          def(
              "/admin/report-recipients",
              "Report Recipients",
              ReportScheduleRecipient.class,
              "reportScheduleId,email",
              "reportScheduleId,email"),
          def(
              "/admin/data-quality",
              "Data Quality",
              ValidationRule.class,
              "code,dataType,fieldName,name,description,minimumValue,maximumValue,changeThreshold,checkMissing,severity,isActive",
              "code,dataType,fieldName,name,minimumValue,maximumValue,severity,isActive"),
          def(
              "/admin/anomalies",
              "Data Anomalies",
              DataAnomaly.class,
              "status",
              "collectionJobId,dataType,fieldName,originalValue,reason,status,detectedAt"),
          def(
              "/admin/audit-logs",
              "Audit Logs",
              AuditEvent.class,
              "",
              "occurredAt,actor,action,entityType,entityReference,result,detail"),
          def(
              "/admin/delivery-monitor",
              "Delivery Monitor",
              ReportDeliveryAttempt.class,
              "",
              "deliveryCode,scheduleName,generatedAt,reportStatus,emailStatus,overallStatus,message"));

  public static Definition get(String path) {
    return ALL.stream()
        .filter(d -> d.path.equals(path))
        .findFirst()
        .orElseThrow(() -> new IllegalArgumentException("Page not found"));
  }

  public static String label(String name) {
    String spaced = name.replaceAll("([a-z])([A-Z])", "$1 $2");
    return Character.toUpperCase(spaced.charAt(0)) + spaced.substring(1);
  }

  private Resources() {}
}
