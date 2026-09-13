# Model files

The full inference pipeline needs four trained `.keras` files: a crop validator
and one disease model for each supported crop. Model binaries are supplied separately
and are not downloaded by cloning this repository.

The mandatory crop gate requires `crop_validator_v2_best.keras` and its matching
`crop_validator_v2_config.json` in this folder. The JSON is versioned; preserve the
saved class order, dimensions and preprocessing settings. Its classes are `cotton`,
`wheat`, `rice` and `out_of_scope`.

The preferred disease-model names are:

| Crop | Preferred model filename | API class order |
|---|---|---|
| Cotton | `cotton_cnn_v3_best.keras` | `bacterial_blight`, `curl_virus`, `healthy` |
| Wheat | `wheat_v2_efficientnetv2b0_best.keras` | `brown_rust`, `healthy`, `yellow_rust` |
| Rice | `rice_v2_efficientnetv2b0_best.keras` | `bacterial_leaf_blight`, `brown_spot`, `healthy`, `leaf_blast`, `tungro` |

The API also recognizes the alternative final filenames listed in `app/config.py`. If your
file has a different name, either rename it or set an environment variable before starting:

```bat
set AGROVISION_WHEAT_MODEL=D:\path\to\wheat_final.keras
```

Use `AGROVISION_COTTON_MODEL`, `AGROVISION_WHEAT_MODEL`, or
`AGROVISION_RICE_MODEL` as needed.

The class order must be identical to the order used during model training. If a training
script used a different order, edit only the relevant `classes` tuple in `app/config.py`.
