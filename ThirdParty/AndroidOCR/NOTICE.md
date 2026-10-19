# Android local OCR

The Android application bundles Google ML Kit Text Recognition v2 (Chinese,
16.0.1), including its native model for supported Android architectures.
Recognition runs on device and does not send resume images or extracted text to
the application's server. ML Kit may collect SDK operational diagnostics under
its own terms.

- SDK documentation and attribution: https://developers.google.com/ml-kit/vision/text-recognition/v2/android
- ML Kit terms: https://developers.google.com/ml-kit/terms
- ML Kit data disclosure: https://developers.google.com/ml-kit/terms/data-disclosure

Dependencies include AndroidX, Kotlin, kotlinx.coroutines, Google Play services
support libraries, Firebase component discovery and Google transport libraries.
Their names, exact versions, original download locations and SHA-256 digests
are pinned in `scripts/android-ocr-dependencies.json`. AndroidX, Kotlin and the
open-source Google support components retain their Apache 2.0 notices; the
Google SDK binaries are subject to their published SDK terms. This list does
not change the license of those components.

Windows uses the installed Windows OCR languages and adds no OCR model to its
installation. Text PDF extraction continues to use the existing PdfPig and
PDFBox Android dependencies and their notices.
