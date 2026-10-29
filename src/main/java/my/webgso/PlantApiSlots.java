package my.webgso;

import java.util.List;

/** Explicit per-plant endpoint fields; identifiers and ownership are resolved on the server. */
public final class PlantApiSlots {
  public record Slot(String field, String apiType, String dataType, String label, String hint) {}

  public static final List<Slot> ALL =
      List.of(
          new Slot(
              "apiRolling",
              "DDQ",
              "Rolling 24 Hour",
              "Rolling 24-hour forecast",
              "A continuous 24-hour forecast."),
          new Slot("apiDayAhead", "DDQ", "Day Ahead", "Day-ahead forecast", "Five days ahead."),
          new Slot("apiWeekAhead", "DDQ", "Week Ahead", "Week-ahead forecast", "Nine days ahead."),
          new Slot(
              "apiFourMonths",
              "DDQ",
              "4 Month Ahead",
              "Four months — complete",
              "The complete four-month forecast."),
          new Slot(
              "apiMonth1",
              "DDQ",
              "4 Month Ahead 1",
              "Four months — month 1",
              "First month of the forecast."),
          new Slot(
              "apiMonth2",
              "DDQ",
              "4 Month Ahead 2",
              "Four months — month 2",
              "Second month of the forecast."),
          new Slot(
              "apiMonth3",
              "DDQ",
              "4 Month Ahead 3",
              "Four months — month 3",
              "Third month of the forecast."),
          new Slot(
              "apiMonth4",
              "DDQ",
              "4 Month Ahead 4",
              "Four months — month 4",
              "Fourth month of the forecast."),
          new Slot(
              "apiMmf", "MMF", "MMF", "Live weather measurements", "MMF readings for this plant."));

  private PlantApiSlots() {}

  public static String datasetWarning(String url, String selectedDataset) {
    if (url == null || url.isBlank()) return "";
    String path;
    try {
      path = java.net.URI.create(url.trim()).getPath();
    } catch (IllegalArgumentException e) {
      return "";
    }
    if (path == null) return "";
    path = path.replaceAll("/+$", "");
    String segment = path.substring(path.lastIndexOf('/') + 1).toLowerCase(java.util.Locale.ROOT);
    String inferred =
        switch (segment) {
          case "dayahead" -> "Day Ahead";
          case "weekahead" -> "Week Ahead";
          case "fourmonthsahead" -> "4 Month Ahead";
          case "fourmonthsahead1" -> "4 Month Ahead 1";
          case "fourmonthsahead2" -> "4 Month Ahead 2";
          case "fourmonthsahead3" -> "4 Month Ahead 3";
          case "fourmonthsahead4" -> "4 Month Ahead 4";
          case "rolling24", "rolling24hour" -> "Rolling 24 Hour";
          case "mmf" -> "MMF";
          default -> "";
        };
    return inferred.isEmpty() || inferred.equals(selectedDataset)
        ? ""
        : "Check dataset: the URL name suggests "
            + inferred
            + ", but this slot is "
            + selectedDataset
            + ". Confirm the dataset with your API provider before collecting data.";
  }
}
