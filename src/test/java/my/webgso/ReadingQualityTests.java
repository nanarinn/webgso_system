package my.webgso;

import static org.assertj.core.api.Assertions.assertThat;

import java.math.BigDecimal;
import java.time.LocalDateTime;
import my.webgso.Models.LssPlant;
import my.webgso.Models.SolarApiReading;
import org.junit.jupiter.api.Test;

class ReadingQualityTests {
  private final LocalDateTime now = LocalDateTime.of(2026, 11, 24, 12, 0);

  @Test
  void marksNormalMmfAndSafeForecastValues() {
    assertThat(ReadingQuality.inspect(reading("MMF", "Humidity", "55", now), plant(), now).tone())
        .isEqualTo("normal");
    assertThat(
            ReadingQuality.inspect(
                    reading("Day Ahead", "ForecastValue", "8.5", now.plusHours(6)), plant(), now)
                .tone())
        .isEqualTo("normal");
  }

  @Test
  void marksImpossibleSensorRangesAsErrors() {
    var humidity = ReadingQuality.inspect(reading("MMF", "humid", "120", now), plant(), now);
    var temperature =
        ReadingQuality.inspect(
            reading("MMF", "MMF1 · Ambient Temperature", "-3276.8", now), plant(), now);
    assertThat(humidity.tone()).isEqualTo("error");
    assertThat(humidity.reason()).contains("0 and 100");
    assertThat(temperature.tone()).isEqualTo("error");
  }

  @Test
  void marksStaleMmfAndCapacityExcessAsWarnings() {
    assertThat(
            ReadingQuality.inspect(
                    reading("MMF", "WindSpeed", "5", now.minusHours(3)), plant(), now)
                .tone())
        .isEqualTo("warning");
    assertThat(
            ReadingQuality.inspect(
                    reading("Day Ahead", "ForecastValue", "15", now), plant(), now)
                .reason())
        .contains("capacity");
  }

  @Test
  void storedApiErrorsTakePriority() {
    var reading = reading("MMF", "Humidity", "55", now);
    reading.errorCode = 502;
    reading.errorMessage = "Bad gateway";
    var quality = ReadingQuality.inspect(reading, plant(), now);
    assertThat(quality.tone()).isEqualTo("error");
    assertThat(quality.reason()).contains("502", "Bad gateway");
  }

  @Test
  void zeroErrorCodeAndOkMessageMeanSuccessfulReading() {
    var reading = reading("MMF", "Humidity", "55", now);
    reading.errorCode = 0;
    reading.errorMessage = "OK";
    assertThat(ReadingQuality.inspect(reading, plant(), now).tone()).isEqualTo("normal");
  }

  private static SolarApiReading reading(
      String dataType, String parameter, String value, LocalDateTime timestamp) {
    var reading = new SolarApiReading();
    reading.dataType = dataType;
    reading.parameter = parameter;
    reading.value = new BigDecimal(value);
    reading.intervalStart = timestamp;
    reading.submissionDateTime = timestamp;
    return reading;
  }

  private static LssPlant plant() {
    var plant = new LssPlant();
    plant.capacityMw = BigDecimal.TEN;
    return plant;
  }
}
