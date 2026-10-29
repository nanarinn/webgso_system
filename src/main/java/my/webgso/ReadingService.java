package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.stereotype.Service;

@Service
public class ReadingService {
  private final Store db;

  public ReadingService(Store db) {
    this.db = db;
  }

  public record AvailableRange(LocalDateTime first, LocalDateTime last, String parameter) {
    public LocalDateTime start() {
      return first.toLocalDate().atStartOfDay();
    }

    public LocalDateTime end() {
      return last.toLocalDate().plusDays(1).atStartOfDay();
    }
  }

  public AvailableRange availableRange(
      String type, Set<Integer> plants, LocalDateTime submission, String parameter, Long jobId) {
    if (plants.isEmpty()) return null;
    var sql =
        new StringBuilder(
                "SELECT MIN(\"IntervalStart\"), MAX(\"IntervalStart\"), MIN(\"Parameter\") FROM"
                    + " \"SolarApiReadings\" WHERE \"DataType\"=? AND \"PlantId\" IN (")
            .append(String.join(",", Collections.nCopies(plants.size(), "?")))
            .append(")");
    var args = new ArrayList<Object>();
    args.add(Policies.canonical(type));
    args.addAll(plants);
    if (submission != null) {
      sql.append(" AND \"SubmissionDateTime\">=? AND \"SubmissionDateTime\"<?");
      args.add(submission);
      args.add(submission.plusMinutes(1));
    }
    if (parameter != null && !parameter.isBlank()) {
      sql.append(" AND UPPER(\"Parameter\")=UPPER(?)");
      args.add(parameter);
    }
    if (jobId != null) {
      sql.append(" AND \"CollectionJobId\"=?");
      args.add(jobId);
    }
    return db.jdbc()
        .query(
            sql.toString(),
            rs -> {
              if (!rs.next() || rs.getTimestamp(1) == null) return null;
              return new AvailableRange(
                  rs.getTimestamp(1).toLocalDateTime(),
                  rs.getTimestamp(2).toLocalDateTime(),
                  rs.getString(3));
            },
            args.toArray());
  }

  public List<SolarApiReading> select(
      String dataType,
      Set<Integer> plants,
      LocalDateTime submission,
      LocalDateTime start,
      LocalDateTime end) {
    StringBuilder where = new StringBuilder("WHERE \"DataType\"=?");
    var args = new ArrayList<Object>();
    args.add(Policies.canonical(dataType));
    if (!plants.isEmpty()) {
      where
          .append(" AND \"PlantId\" IN (")
          .append(String.join(",", Collections.nCopies(plants.size(), "?")))
          .append(")");
      args.addAll(plants);
    }
    if (submission != null) {
      where.append(" AND \"SubmissionDateTime\">=? AND \"SubmissionDateTime\"<?");
      args.add(submission);
      args.add(submission.plusMinutes(1));
    }
    if (start != null) {
      where.append(" AND \"IntervalStart\">=?");
      args.add(start);
    }
    if (end != null) {
      where.append(" AND \"IntervalStart\"<?");
      args.add(end);
    }
    where.append(" ORDER BY \"Id\" DESC LIMIT 250001");
    var raw = db.list(SolarApiReading.class, where.toString(), args.toArray());
    if (raw.size() > 250000)
      throw new IllegalArgumentException(
          "The selected range is too large. Choose fewer plants or a shorter date range.");
    var latest = new LinkedHashMap<String, SolarApiReading>();
    for (var r : raw)
      latest.putIfAbsent(
          r.plantId + "|" + r.parameter.toUpperCase(Locale.ROOT) + "|" + r.intervalStart, r);
    return latest.values().stream()
        .sorted(
            Comparator.comparing((SolarApiReading r) -> r.intervalStart)
                .thenComparing(r -> r.plantId)
                .thenComparing(r -> r.parameter))
        .toList();
  }

  public static Set<Integer> plantIds(String value) {
    if (value == null || value.isBlank() || value.equals("all")) return Set.of();
    var result = new LinkedHashSet<Integer>();
    try {
      for (String id : value.split(",")) result.add(Integer.parseInt(id.trim()));
    } catch (Exception e) {
      throw new IllegalArgumentException("Invalid plant selection");
    }
    if (result.size() > 500) throw new IllegalArgumentException("Too many plants selected");
    return result;
  }
}
