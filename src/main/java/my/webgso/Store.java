package my.webgso;

import java.lang.reflect.Field;
import java.math.BigDecimal;
import java.sql.*;
import java.time.*;
import java.util.*;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.support.GeneratedKeyHolder;
import org.springframework.stereotype.Repository;

/** Only internal, constant predicates are accepted here; all request values are bind parameters. */
@Repository
public class Store {
  private final JdbcTemplate jdbc;

  public Store(JdbcTemplate jdbc) {
    this.jdbc = jdbc;
  }

  public JdbcTemplate jdbc() {
    return jdbc;
  }

  public static String q(String name) {
    return "\"" + name.replace("\"", "\"\"") + "\"";
  }

  public static String column(Field f) {
    return Character.toUpperCase(f.getName().charAt(0)) + f.getName().substring(1);
  }

  public static String table(Class<?> type) {
    var name = Tables.NAMES.get(type);
    if (name == null) throw new IllegalArgumentException("Unknown record type");
    return q(name);
  }

  public <T> List<T> list(Class<T> type, String predicate, Object... args) {
    return jdbc.query(
        "SELECT * FROM " + table(type) + " " + predicate, (rs, index) -> read(type, rs), args);
  }

  public <T> Optional<T> find(Class<T> type, Object id) {
    return list(type, "WHERE \"Id\"=?", id).stream().findFirst();
  }

  public long count(Class<?> type) {
    return jdbc.queryForObject("SELECT COUNT(*) FROM " + table(type), Long.class);
  }

  public <T> T insert(T record) {
    Class<?> type = record.getClass();
    List<Field> fields = new ArrayList<>(List.of(type.getFields()));
    boolean generated = type != Models.ApplicationUser.class;
    if (generated) fields.removeIf(f -> f.getName().equals("id"));
    String columns = String.join(",", fields.stream().map(f -> q(column(f))).toList());
    String marks = String.join(",", Collections.nCopies(fields.size(), "?"));
    var keys = new GeneratedKeyHolder();
    jdbc.update(
        connection -> {
          var statement =
              connection.prepareStatement(
                  "INSERT INTO " + table(type) + " (" + columns + ") VALUES (" + marks + ")",
                  new String[] {"Id"});
          for (int i = 0; i < fields.size(); i++) {
            try {
              statement.setObject(i + 1, fields.get(i).get(record));
            } catch (IllegalAccessException e) {
              throw new IllegalStateException(e);
            }
          }
          return statement;
        },
        keys);
    if (generated) {
      try {
        var id = type.getField("id");
        var key = keys.getKey();
        id.set(record, id.getType() == Long.class ? (Object) key.longValue() : key.intValue());
      } catch (ReflectiveOperationException e) {
        throw new IllegalStateException(e);
      }
    }
    return record;
  }

  public <T> T update(T record) {
    try {
      var fields =
          Arrays.stream(record.getClass().getFields())
              .filter(f -> !f.getName().equals("id"))
              .toList();
      String assignments = String.join(",", fields.stream().map(f -> q(column(f)) + "=?").toList());
      var args = new ArrayList<Object>();
      for (var field : fields) args.add(field.get(record));
      args.add(record.getClass().getField("id").get(record));
      if (jdbc.update(
              "UPDATE " + table(record.getClass()) + " SET " + assignments + " WHERE \"Id\"=?",
              args.toArray())
          != 1) throw new IllegalArgumentException("Record no longer exists");
      return record;
    } catch (ReflectiveOperationException e) {
      throw new IllegalStateException(e);
    }
  }

  private static <T> T read(Class<T> type, ResultSet rs) throws SQLException {
    try {
      T record = type.getConstructor().newInstance();
      for (Field field : type.getFields()) {
        Object value = rs.getObject(column(field));
        if (value != null) {
          Class<?> target = field.getType();
          if (target == OffsetDateTime.class)
            value = rs.getObject(column(field), OffsetDateTime.class);
          else if (target == LocalDateTime.class)
            value = rs.getObject(column(field), LocalDateTime.class);
          else if (target == LocalDate.class) value = rs.getObject(column(field), LocalDate.class);
          else if (target == Long.class && value instanceof Number n) value = n.longValue();
          else if (target == Integer.class && value instanceof Number n) value = n.intValue();
          else if (target == Double.class && value instanceof Number n) value = n.doubleValue();
          else if (target == BigDecimal.class && !(value instanceof BigDecimal))
            value = new BigDecimal(value.toString());
        }
        field.set(record, value);
      }
      return record;
    } catch (ReflectiveOperationException e) {
      throw new IllegalStateException(e);
    }
  }
}
