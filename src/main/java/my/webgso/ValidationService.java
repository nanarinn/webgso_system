package my.webgso;

import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import my.webgso.PayloadTranslator.Issue;
import org.springframework.stereotype.Service;

@Service
public class ValidationService {
  private final Store db;

  public ValidationService(Store db) {
    this.db = db;
  }

  public List<ValidationRule> rules() {
    return db.list(ValidationRule.class, "WHERE \"IsActive\"=true");
  }

  public static boolean matches(ValidationRule rule, String parameter) {
    return rule.fieldName == null
        || rule.fieldName.isBlank()
        || Set.of("*", "All").contains(rule.fieldName)
        || rule.fieldName.equalsIgnoreCase(parameter);
  }

  public static String problem(SolarApiReading r, BigDecimal capacity, List<ValidationRule> rules) {
    if (r == null) return "Missing reading";
    for (var rule : rules) {
      if (!Boolean.TRUE.equals(rule.isActive)
          || !rule.dataType.equals(r.dataType.equals("MMF") ? "MMF" : "DDQ")
          || !rule.ruleSource.equals("Fixed") && !matches(rule, r.parameter)) continue;
      if (rule.code.equals("DDQ-004")
          && r.value.compareTo(Objects.requireNonNullElse(rule.minimumValue, BigDecimal.ZERO)) < 0)
        return "Value below minimum";
      if (rule.code.equals("DDQ-005") && capacity.signum() > 0 && r.value.compareTo(capacity) > 0)
        return "Value exceeds plant capacity";
      if (!rule.ruleSource.equals("Fixed")) {
        if (rule.minimumValue != null && r.value.compareTo(rule.minimumValue) < 0)
          return "Value below configured minimum";
        if (rule.maximumValue != null && r.value.compareTo(rule.maximumValue) > 0)
          return "Value exceeds configured maximum";
      }
    }
    return null;
  }

  public List<Issue> evaluate(
      List<SolarApiReading> readings, String apiType, BigDecimal capacity, List<Issue> translated) {
    var rules = rules();
    var issues = new ArrayList<Issue>();
    var codes = new HashSet<String>();
    rules.forEach(r -> codes.add(r.code));
    translated.stream().filter(i -> codes.contains(i.code())).forEach(issues::add);
    for (var rule : rules) {
      if (!rule.dataType.equals(apiType)) continue;
      var selected =
          readings.stream()
              .filter(r -> rule.ruleSource.equals("Fixed") || matches(rule, r.parameter))
              .sorted(Comparator.comparing(r -> r.intervalStart))
              .toList();
      var previous = new HashMap<String, SolarApiReading>();
      for (var reading : selected) {
        String reason = null;
        if (rule.code.equals("DDQ-004") && reading.value.signum() < 0)
          reason = "Negative forecast value";
        if (rule.code.equals("DDQ-005")
            && capacity.signum() > 0
            && reading.value.compareTo(capacity) > 0)
          reason = "Forecast exceeds registered plant capacity";
        if (!rule.ruleSource.equals("Fixed")) {
          if (rule.minimumValue != null && reading.value.compareTo(rule.minimumValue) < 0)
            reason = "Value below configured minimum " + rule.minimumValue;
          if (rule.maximumValue != null && reading.value.compareTo(rule.maximumValue) > 0)
            reason = "Value exceeds configured maximum " + rule.maximumValue;
          var before = previous.get(reading.parameter);
          if (before != null
              && rule.changeThreshold != null
              && reading.value.subtract(before.value).abs().compareTo(rule.changeThreshold) > 0)
            reason = "Change exceeds configured hysteresis " + rule.changeThreshold;
          if (before != null
              && Boolean.TRUE.equals(rule.checkMissing)
              && Duration.between(before.intervalStart, reading.intervalStart).toMinutes()
                  > reading.intervalMinutes)
            issues.add(
                new Issue(
                    rule.code,
                    reading.parameter,
                    null,
                    "Missing values between "
                        + before.intervalStart
                        + " and "
                        + reading.intervalStart));
        }
        if (reason != null)
          issues.add(
              new Issue(rule.code, reading.parameter, reading.value.toPlainString(), reason));
        previous.put(reading.parameter, reading);
      }
      if (selected.isEmpty() && Boolean.TRUE.equals(rule.checkMissing))
        issues.add(
            new Issue(
                rule.code, rule.fieldName, null, "No value returned for configured parameter"));
    }
    return issues.stream().distinct().toList();
  }
}
