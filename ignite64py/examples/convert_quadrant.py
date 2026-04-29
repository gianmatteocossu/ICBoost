from pathlib import Path

from icboost.config import rewrite_full_configuration_quadrant


def main() -> None:
    base = Path(__file__).resolve().parents[1] / "ConfigurationFiles"
    src = base / "IGNITE64_configNW_26.04.28.10.40.01.txt"
    dst = base / "IGNITE64_configSW_26.04.28.10.40.01.txt"

    rewrite_full_configuration_quadrant(src, dst, "SW")
    print("Wrote:", dst)


if __name__ == "__main__":
    main()

