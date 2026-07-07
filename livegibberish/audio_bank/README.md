Audio bank recordings live under `users/<user_id>/`.

Each user folder has:

- `manifest.json`: maps normalized whitelist words and gibberish duration buckets to WAV files.
- `samples/whitelist/<word>.wav`: one preprocessed clip per normalized whitelist word.
- `samples/gibberish/<short|medium|long>/<name>.wav`: preprocessed replacement clips by duration bucket.

The runtime loads a `SampleBank` from the manifest before live processing starts. Word replacement uses preloaded PCM buffers from memory and does not read WAV files from disk per word.
