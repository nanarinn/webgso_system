package my.webgso;

import java.util.Map;

public final class Tables {
  public static final Map<Class<?>, String> NAMES =
      Map.ofEntries(
          Map.entry(Models.ApplicationUser.class, "AspNetUsers"),
          Map.entry(Models.LssPlant.class, "LssPlants"),
          Map.entry(Models.PlantApiConfiguration.class, "PlantApiConfigurations"),
          Map.entry(Models.AuditEvent.class, "AuditEvents"),
          Map.entry(Models.CollectionJobEntity.class, "CollectionJobs"),
          Map.entry(Models.RawApiResponse.class, "RawApiResponses"),
          Map.entry(Models.ValidationRule.class, "ValidationRules"),
          Map.entry(Models.DataAnomaly.class, "DataAnomalies"),
          Map.entry(Models.CollectionSchedule.class, "CollectionSchedules"),
          Map.entry(Models.ReportExecution.class, "ReportExecutions"),
          Map.entry(Models.SolarApiReading.class, "SolarApiReadings"),
          Map.entry(Models.ReportScheduleEntity.class, "ReportSchedules"),
          Map.entry(Models.ReportScheduleRecipient.class, "ReportScheduleRecipients"),
          Map.entry(Models.ReportDeliveryAttempt.class, "ReportDeliveryAttempts"),
          Map.entry(Models.ReportDeliveryRecipient.class, "ReportDeliveryRecipients"),
          Map.entry(Models.UserAccessNotification.class, "UserAccessNotifications"));

  private Tables() {}
}
