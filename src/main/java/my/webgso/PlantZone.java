package my.webgso;

import java.util.Locale;
import java.util.Objects;

/** Zone assigned by the plant's state for both the editor and persistence. */
public final class PlantZone {
  private PlantZone() {}

  public static String forState(String state) {
    return switch (Objects.toString(state, "").strip().toLowerCase(Locale.ROOT)) {
      case "kedah", "perlis", "penang", "pulau pinang", "perak" -> "Northern";
      case "selangor", "negeri sembilan", "kuala lumpur", "putrajaya" -> "Central";
      case "johor", "melaka" -> "Southern";
      case "pahang", "terengganu", "kelantan" -> "Eastern";
      case "sabah", "labuan" -> "Sabah";
      case "sarawak" -> "Sarawak";
      default -> "";
    };
  }
}
