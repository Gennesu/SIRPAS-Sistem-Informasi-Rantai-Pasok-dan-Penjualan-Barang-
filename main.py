"""Jalankan aplikasi Platform Demo dengan .NET SDK yang dibutuhkan."""

import os
import ipaddress
from pathlib import Path
import shutil
import socket
import subprocess
import sys


ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / "src" / "Platform.Web" / "Platform.Web.csproj"
REQUIRED_SDK_MAJOR = 10
SDK_DOWNLOAD_URL = "https://dotnet.microsoft.com/download/dotnet/10.0"


def has_required_sdk(dotnet: str) -> bool:
    result = subprocess.run(
        [dotnet, "--list-sdks"],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        return False

    for line in result.stdout.splitlines():
        version = line.strip().split(maxsplit=1)[0] if line.strip() else ""
        if version.split(".", maxsplit=1)[0] == str(REQUIRED_SDK_MAJOR):
            return True
    return False


def get_lan_ipv4_addresses() -> list[str]:
    """Return usable local IPv4 addresses, preferring the default network route."""
    addresses: list[str] = []

    # A UDP connect selects the interface used for this route without sending a packet.
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as route_socket:
            route_socket.connect(("192.0.2.1", 80))
            addresses.append(route_socket.getsockname()[0])
    except OSError:
        pass

    try:
        addresses.extend(socket.gethostbyname_ex(socket.gethostname())[2])
    except OSError:
        pass

    usable: list[str] = []
    for value in addresses:
        try:
            address = ipaddress.IPv4Address(value)
        except ipaddress.AddressValueError:
            continue
        if address.is_loopback or address.is_link_local or address.is_unspecified or address.is_multicast:
            continue
        normalized = str(address)
        if normalized not in usable:
            usable.append(normalized)

    return usable


def print_access_urls(lan_mode: bool, port: int) -> None:
    print("Alamat aplikasi:", flush=True)
    print(f"  Komputer ini: http://localhost:{port}", flush=True)

    if not lan_mode:
        print("  Untuk akses dari HP, jalankan: python main.py --lan", flush=True)
        return

    addresses = get_lan_ipv4_addresses()
    if addresses:
        for address in addresses:
            print(f"  Wi-Fi/LAN: http://{address}:{port}", flush=True)
    else:
        print("  IP jaringan tidak terdeteksi. Periksa alamat IPv4 dengan perintah ipconfig.", flush=True)
    print("  Pastikan HP terhubung ke Wi-Fi yang sama.", flush=True)


def find_available_port(start_port: int, lan_mode: bool) -> int:
    host = "0.0.0.0" if lan_mode else "127.0.0.1"
    for port in range(start_port, start_port + 100):
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as port_socket:
                port_socket.bind((host, port))
            return port
        except OSError:
            continue
    raise OSError(f"Tidak ada port kosong mulai dari {start_port}.")


def main() -> int:
    if not PROJECT.is_file():
        print(f"File proyek tidak ditemukan: {PROJECT}", file=sys.stderr)
        return 1

    local_dotnet = ROOT / ".dotnet-sdk" / (
        "dotnet.exe" if os.name == "nt" else "dotnet"
    )
    dotnet = str(local_dotnet) if local_dotnet.is_file() else shutil.which("dotnet")
    if dotnet is None:
        print(".NET 10 SDK belum terpasang.", file=sys.stderr)
        print(f"Unduh dari: {SDK_DOWNLOAD_URL}", file=sys.stderr)
        return 1

    if not has_required_sdk(dotnet):
        print(".NET 10 SDK belum ditemukan (aplikasi ini menargetkan net10.0).", file=sys.stderr)
        print(f"Unduh dan pasang dari: {SDK_DOWNLOAD_URL}", file=sys.stderr)
        return 1

    env = os.environ.copy()
    env["ASPNETCORE_ENVIRONMENT"] = "Development"
    env["NUGET_PACKAGES"] = str(ROOT / ".nuget" / "packages")
    env["Database__Provider"] = "Sqlite"
    env["ConnectionStrings__Default"] = "Data Source=platform-demo-v3.db;Cache=Shared;Default Timeout=30"

    run_args = list(sys.argv[1:])
    lan_mode = "--lan" in run_args
    if lan_mode:
        run_args.remove("--lan")
    separator = run_args.index("--") if "--" in run_args else len(run_args)
    app_args = run_args[separator + 1:]
    has_custom_urls = any(arg == "--urls" or arg.startswith("--urls=") for arg in app_args)
    port = 5080
    if not has_custom_urls:
        try:
            port = find_available_port(port, lan_mode)
        except OSError as ex:
            print(str(ex), file=sys.stderr)
            return 1
        if port != 5080:
            print(f"Port 5080 sedang dipakai; aplikasi memakai port {port}.", flush=True)
        bind_address = "0.0.0.0" if lan_mode else "localhost"
        urls_arg = f"--urls=http://{bind_address}:{port}"
        if separator < len(run_args):
            run_args.insert(separator + 1, urls_arg)
        else:
            run_args.extend(["--", urls_arg])

    command = [dotnet, "run", "--project", str(PROJECT), "--no-launch-profile", *run_args]
    print_access_urls(lan_mode, port)
    try:
        return subprocess.call(command, cwd=PROJECT.parent, env=env)
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
