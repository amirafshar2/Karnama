"""Create .env from .env.example with fresh random secrets (never commit .env).

Usage: python tools/new-env.py [--force]
"""
import secrets
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / ".env.example"
DST = ROOT / ".env"

GENERATED = {
    "Jwt__SigningKey": lambda: secrets.token_urlsafe(48),
    "Otp__HashKey": lambda: secrets.token_urlsafe(48),
    "Setup__Token": lambda: secrets.token_urlsafe(18),
    "POSTGRES_PASSWORD": lambda: secrets.token_urlsafe(24),
}


def main() -> None:
    if DST.exists() and "--force" not in sys.argv:
        print(".env already exists (use --force to overwrite).")
        return
    values = {k: f() for k, f in GENERATED.items()}
    out = []
    for line in SRC.read_text(encoding="utf-8").splitlines():
        key = line.split("=", 1)[0].strip()
        if key in values:
            line = f"{key}={values[key]}"
        elif key == "ConnectionStrings__Default":
            line = ("ConnectionStrings__Default=Host=localhost;Port=5432;Database=karnama;Username=karnama;"
                    f"Password={values['POSTGRES_PASSWORD']};Maximum Pool Size=100")
        out.append(line)
    DST.write_text("\n".join(out) + "\n", encoding="utf-8")
    print("OK -> .env")
    print("Setup token (for /setup):", values["Setup__Token"])


if __name__ == "__main__":
    main()
