package my.webgso;

import java.math.BigDecimal;
import java.time.*;

/** Relational records mapped to the existing PostgreSQL schema. */
public final class Models {
  private Models() {}

  public static class AuditEvent {
    public Long id = 0L;
    public OffsetDateTime occurredAt = OffsetDateTime.now(ZoneOffset.UTC);
    public String actor = "System";
    public String action = "";
    public String entityType;
    public String entityReference;
    public String result = "Success";
    public String detail;
    public String correlationId;
  }

  public static class LssPlant {
    public Integer id = 0;
    public String plantId = "";
    public String name = "";
    public String state = "";
    public BigDecimal capacityMw = BigDecimal.ZERO;
    public String ddqApiEndpoint = "";
    public String mmfApiEndpoint = "";
    public Boolean isActive = true;
    public String apiConnectionStatus = "Not tested";
    public String lastApiTestSource;
    public String lastApiTestMessage;
    public OffsetDateTime lastApiTestAt;
    public OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC);
  }

  public static class CollectionJobEntity {
    public Long id = 0L;
    public String collectionId = "";
    public Integer plantId = 0;
    public Integer collectionScheduleId;
    public String dataType = "DDQ";
    public String mode = "Manual";
    public LocalDate submissionDate = LocalDate.now();
    public LocalDateTime submissionDateTime = LocalDateTime.now();
    public OffsetDateTime startedAt = OffsetDateTime.now(ZoneOffset.UTC);
    public OffsetDateTime completedAt;
    public String status = "Pending";
    public String errorMessage;
    public String responseMessage = "";
    public Double durationSeconds = 0.0;
    public String requestedBy = "System";
    public Integer retryCount = 0;
    public Boolean replaceExisting = false;
  }

  public static class RawApiResponse {
    public Long id = 0L;
    public Long collectionJobId = 0L;
    public OffsetDateTime receivedAt = OffsetDateTime.now(ZoneOffset.UTC);
    public Integer httpStatusCode = 0;
    public String contentType = "application/json";
    public String payload = "";
    public String sha256 = "";
  }

  public static class ValidationRule {
    public Integer id = 0;
    public String code = "";
    public String dataType = "DDQ";
    public String fieldName = "";
    public String name = "";
    public String description = "";
    public String condition = "";
    public String operator = "";
    public BigDecimal minimumValue;
    public BigDecimal maximumValue;
    public BigDecimal changeThreshold;
    public Boolean checkMissing = false;
    public String ruleSource = "Fixed";
    public String severity = "Error";
    public Boolean isActive = true;
    public String createdBy;
    public OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC);
    public OffsetDateTime updatedAt;
  }

  public static class DataAnomaly {
    public Long id = 0L;
    public Long collectionJobId = 0L;
    public Integer validationRuleId;
    public String dataType = "";
    public String fieldName = "";
    public String originalValue;
    public String reason = "";
    public String status = "Open";
    public OffsetDateTime detectedAt = OffsetDateTime.now(ZoneOffset.UTC);
  }

  public static class CollectionSchedule {
    public Integer id = 0;
    public Integer plantId = 0;
    public String scheduleCode = "";
    public String name = "";
    public String apiType = "DDQ";
    public String dataType = "Rolling 24 Hour";
    public String interval = "15 minutes";
    public String runTime = "00:15";
    public String scheduleDay = "";
    public String scheduleMonths = "";
    public String cronExpression = "";
    public String timeZoneId = "Asia/Kuala_Lumpur";
    public Integer retryCount = 3;
    public Boolean isActive = true;
  }

  public static class ReportExecution {
    public Long id = 0L;
    public String reportId = "";
    public String reportType = "OnDemand";
    public String dataType = "DDQ";
    public String requestedBy = "";
    public OffsetDateTime requestedAt = OffsetDateTime.now(ZoneOffset.UTC);
    public String generationStatus = "Pending";
    public String deliveryStatus = "NotRequired";
    public String mainFilePath;
    public String anomalyFilePath;
    public String errorMessage;
    public String name = "";
    public LocalDateTime submissionDate = LocalDateTime.now();
    public String dataSelection = "";
    public Integer intervalMinutes = 15;
    public LocalDateTime dataRangeStart;
    public LocalDateTime dataRangeEnd;
    public String description = "";
    public Integer plantCount = 0;
    public Integer recordCount = 0;
    public Integer anomalyCount = 0;
  }

  public static class ReportScheduleEntity {
    public Integer id = 0;
    public String scheduleCode = "";
    public String name = "";
    public String apiType = "DDQ";
    public String dataType = "Rolling 24 Hour";
    public Integer intervalMinutes = 15;
    public String scheduleType = "Daily";
    public String scheduleDay = "";
    public String scheduleMonths = "";
    public String executionTime = "07:00";
    public Boolean isActive = true;
    public OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC);
  }

  public static class ReportScheduleRecipient {
    public Integer id = 0;
    public Integer reportScheduleId = 0;
    public String email = "";
  }

  public static class ReportDeliveryAttempt {
    public Long id = 0L;
    public String deliveryCode = "";
    public Integer reportScheduleId;
    public String scheduleName = "";
    public String apiType = "DDQ";
    public OffsetDateTime generatedAt = OffsetDateTime.now(ZoneOffset.UTC);
    public String reportStatus = "Pending";
    public String emailStatus = "Not sent";
    public String overallStatus = "Pending";
    public String message = "";
  }

  public static class ReportDeliveryRecipient {
    public Long id = 0L;
    public Long reportDeliveryAttemptId = 0L;
    public String email = "";
  }

  public static class PlantApiConfiguration {
    public Integer id = 0;
    public Integer plantId = 0;
    public String apiType = "DDQ";
    public String dataType = "";
    public String apiLink = "";
  }

  public static class SolarApiReading {
    public Long id = 0L;
    public Long collectionJobId = 0L;
    public Integer plantId = 0;
    public String dataType = "";
    public LocalDateTime submissionDateTime = LocalDateTime.now();
    public LocalDateTime intervalStart = LocalDateTime.now();
    public LocalDateTime intervalEnd;
    public Integer intervalMinutes;
    public String parameter = "";
    public BigDecimal value = BigDecimal.ZERO;
    public String unit = "";
    public Integer errorCode;
    public String errorMessage;
    public Boolean isSynthetic = false;
    public String syntheticReason;
  }

  public static class UserAccessNotification {
    public Long id = 0L;
    public String userId = "";
    public String relatedUserId;
    public String email = "";
    public String notificationType = "";
    public String subject = "";
    public String body = "";
    public String deliveryStatus = "";
    public String errorMessage;
    public OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC);
    public OffsetDateTime sentAt;
    public OffsetDateTime readAt;
  }

  public static class ApplicationUser {
    public String id = null;
    public String userName = "";
    public String normalizedUserName = "";
    public String email = "";
    public String normalizedEmail = "";
    public Boolean emailConfirmed = false;
    public String passwordHash = null;
    public String securityStamp = "";
    public String concurrencyStamp = "";
    public String phoneNumber = null;
    public Boolean phoneNumberConfirmed = false;
    public Boolean twoFactorEnabled = false;
    public OffsetDateTime lockoutEnd = null;
    public Boolean lockoutEnabled = true;
    public Integer accessFailedCount = 0;
    public String displayName = "";
    public String accessStatus = "Pending";
    public Boolean isEntraUser = false;
    public String entraObjectId;
    public OffsetDateTime accessRequestedAt;
    public OffsetDateTime accessReviewedAt;
    public String accessReviewedBy;
    public String accessReviewNote;
  }
}
