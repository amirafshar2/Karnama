"""Create .env from .env.example with fresh random secrets (never commit .env).

Usage: python tools/new-env.py [--force]          development .env
       python tools/new-env.py --prod [--force]   deploy/.env.prod for the server
       python tools/new-env.py --gateway [--force] deploy/gateway/.env for the Telegram gateway (outside Iran)
"""
import base64
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
    "Vault__MasterKey": lambda: base64.b64encode(secrets.token_bytes(32)).decode(),
    "POSTGRES_PASSWORD": lambda: secrets.token_urlsafe(24),
}


PROD_GENERATED = {
    "POSTGRES_PASSWORD": lambda: secrets.token_urlsafe(24),
    "JWT_SIGNING_KEY": lambda: secrets.token_urlsafe(48),
    "OTP_HASH_KEY": lambda: secrets.token_urlsafe(48),
    "SETUP_TOKEN": lambda: secrets.token_urlsafe(18),
    "VAULT_MASTER_KEY": lambda: base64.b64encode(secrets.token_bytes(32)).decode(),
    "BALE_WEBHOOK_SECRET": lambda: secrets.token_urlsafe(24),
}


def prod() -> None:
    src, dst = ROOT / "deploy" / ".env.prod.example", ROOT / "deploy" / ".env.prod"
    if dst.exists() and "--force" not in sys.argv:
        print("deploy/.env.prod already exists (use --force to overwrite).")
        return
    values = {k: f() for k, f in PROD_GENERATED.items()}
    out = [f"{l.split('=', 1)[0]}={values[l.split('=', 1)[0]]}" if l.split("=", 1)[0] in values else l for l in src.read_text(encoding="utf-8").splitlines()]
    dst.write_text("\n".join(out) + "\n", encoding="utf-8")
    print("OK -> deploy/.env.prod  (now fill SITE_DOMAIN, API_DOMAIN and the SMS/Bale values; chmod 600)")
    print("Setup token (for /setup):", values["SETUP_TOKEN"])
    print("IMPORTANT: copy VAULT_MASTER_KEY somewhere safe and separate from the database backups;")
    print("without it the stored client passwords can never be opened again.")


def gateway() -> None:
    src, dst = ROOT / "deploy" / "gateway" / ".env.example", ROOT / "deploy" / "gateway" / ".env"
    if dst.exists() and "--force" not in sys.argv:
        print("deploy/gateway/.env already exists (use --force to overwrite).")
        return
    values = {
        "Telegram__WebhookSecret": secrets.token_urlsafe(32).replace("-", "_"),
        "Telegram__WebhookPath": secrets.token_urlsafe(24),
        "Gateway__Secret": secrets.token_urlsafe(48),
    }
    out = [f"{l.split('=', 1)[0]}={values[l.split('=', 1)[0]]}" if l.split("=", 1)[0] in values else l for l in src.read_text(encoding="utf-8").splitlines()]
    dst.write_text("\n".join(out) + "\n", encoding="utf-8")
    print("OK -> deploy/gateway/.env  (fill GATEWAY_DOMAIN, the bot token from @BotFather and Gateway__IranApiUrl)")
    print("Put this on the Iran server as TELEGRAM_GATEWAY_SECRET:", values["Gateway__Secret"])


def main() -> None:
    if "--prod" in sys.argv:
        return prod()
    if "--gateway" in sys.argv:
        return gateway()
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
