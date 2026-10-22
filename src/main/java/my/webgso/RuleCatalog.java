package my.webgso;

import java.util.List;

public final class RuleCatalog {
  public record Definition(
      String code,
      String apiType,
      String name,
      String description,
      String condition,
      String severity) {}

  public static final List<Definition> DEFINITIONS =
      List.of(
          new Definition(
              "DDQ-001",
              "DDQ",
              "Missing interval reading",
              "Every plant must contain a forecast value for each expected timestamp in the"
                  + " selected interval.",
              "Missing or null value",
              "Error"),
          new Definition(
              "DDQ-002",
              "DDQ",
              "Duplicate forecast reading",
              "Only one forecast reading is allowed for the same submission date and time, plant,"
                  + " data type and data timestamp.",
              "Duplicate submission datetime composite key",
              "Error"),
          new Definition(
              "DDQ-003",
              "DDQ",
              "Invalid timestamp interval",
              "Forecast timestamps must align with the configured data interval.",
              "Timestamp not on interval",
              "Error"),
          new Definition(
              "DDQ-005",
              "DDQ",
              "Above plant capacity",
              "A forecast value cannot be greater than the configured capacity of its plant.",
              "Value > plant capacity",
              "Error"),
          new Definition(
              "DDQ-006",
              "DDQ",
              "Unexpected night generation",
              "A material solar generation value during expected night hours may indicate an"
                  + " incorrect timestamp or forecast.",
              "Night value > 2% capacity",
              "Error"),
          new Definition(
              "DDQ-008",
              "DDQ",
              "Flat forecast profile",
              "An unchanged non-zero forecast over a long window can indicate copied or stale"
                  + " data.",
              "Same value for 8 intervals",
              "Error"),
          new Definition(
              "DDQ-009",
              "DDQ",
              "Missing API timestamps",
              "Timestamps must remain continuous at the source interval detected from the API"
                  + " response.",
              "One or more intervals missing inside the supplied range",
              "Error"),
          new Definition(
              "DDQ-010",
              "DDQ",
              "Invalid data type or unit",
              "The data type must be supported and forecast power must use the expected MW unit.",
              "Unsupported type or unit",
              "Error"),
          new Definition(
              "DDQ-011",
              "DDQ",
              "Submission timestamp mismatch",
              "Forecast timestamps must fall within the valid horizon for the submission date and"
                  + " selected DDQ data type.",
              "Timestamp outside horizon",
              "Error"),
          new Definition(
              "DDQ-012",
              "DDQ",
              "Stale or future submission",
              "A submission cannot be unreasonably old or dated ahead of the system time.",
              "Outside submission window",
              "Error"),
          new Definition(
              "MMF-001",
              "MMF",
              "Missing interval reading",
              "Every configured parameter must contain a reading at each source interval.",
              "Missing or null value",
              "Error"),
          new Definition(
              "MMF-002",
              "MMF",
              "Duplicate sensor reading",
              "Only one averaged reading is allowed for the same submission date and time, plant,"
                  + " parameter and data timestamp.",
              "Duplicate submission datetime composite key",
              "Error"),
          new Definition(
              "MMF-003",
              "MMF",
              "Invalid timestamp interval",
              "MMF timestamps must align with the detected source interval.",
              "Timestamp not on source interval",
              "Error"),
          new Definition(
              "MMF-016",
              "MMF",
              "Stale or future measurement",
              "Measurements cannot be unreasonably old or dated ahead of the collection time.",
              "Timestamp outside collection window",
              "Error"));

  private RuleCatalog() {}
}
