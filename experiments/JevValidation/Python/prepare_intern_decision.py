"""Download fixed official Intern-Decision checkpoints and verify their bytes."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request


MODELS = {
    "0.8B": ("internlm/Intern-Decision-0.8B", "42457fafd13d10d0b0d9be46cb30a23caa409994"),
    "2B": ("internlm/Intern-Decision-2B", "85c5657708a09884ff9fd8d7f57789b410a97b33"),
}


def verify_file(path, descriptor):
    size = path.stat().st_size
    if size != descriptor["size"]:
        raise ValueError("File size mismatch: " + path.name)
    sha = hashlib.sha256()
    git = hashlib.sha1(b"blob " + str(size).encode() + b"\0")
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            sha.update(chunk)
            git.update(chunk)
    expected = descriptor.get("lfs", {}).get("sha256")
    actual = sha.hexdigest() if expected else git.hexdigest()
    expected = expected or descriptor["blobId"]
    if actual != expected:
        raise ValueError("Repository hash mismatch: " + path.name)
    return {"file": path.name, "bytes": size, "sha256": sha.hexdigest(),
            "repository_hash": expected}


def prepare(size, output):
    from huggingface_hub import snapshot_download

    model_id, revision = MODELS[size]
    url = f"https://huggingface.co/api/models/{model_id}/revision/{revision}?blobs=true"
    with urllib.request.urlopen(url, timeout=30) as response:
        metadata = json.load(response)
    if metadata["sha"] != revision:
        raise ValueError("Resolved revision differs from the pinned checkpoint")
    descriptors = [entry for entry in metadata["siblings"] if
                   entry["rfilename"].endswith((".safetensors", ".json", ".jinja", ".txt"))
                   and entry["rfilename"] != "requirements.txt"]
    if any(Path(entry["rfilename"]).name != entry["rfilename"] for entry in descriptors):
        raise ValueError("Checkpoint files must have flat names")
    names = [entry["rfilename"] for entry in descriptors]
    required = {"config.json", "tokenizer.json", "tokenizer_config.json", "chat_template.jinja",
                "model.safetensors.index.json", "preprocessor_config.json"}
    if not required.issubset(names):
        raise ValueError("Official checkpoint metadata is incomplete")
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    snapshot_download(model_id, revision=revision, token=False, local_dir=output,
                      allow_patterns=names, max_workers=3)
    files = [verify_file(output / entry["rfilename"], entry) for entry in descriptors]
    index = json.loads((output / "model.safetensors.index.json").read_text())
    if not set(index["weight_map"].values()).issubset(names):
        raise ValueError("A checkpoint weight shard is missing")
    report = {"schema_version": 1, "model_id": model_id, "revision": revision,
              "metadata_url": url, "files": files,
              "weight_bytes": sum(entry["bytes"] for entry in files if entry["file"].endswith(".safetensors")),
              "total_bytes": sum(entry["bytes"] for entry in files)}
    (output / "download-provenance.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--size", choices=MODELS, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = prepare(args.size, args.output)
    print(json.dumps({key: value for key, value in report.items() if key != "files"}, indent=2))


if __name__ == "__main__":
    main()
