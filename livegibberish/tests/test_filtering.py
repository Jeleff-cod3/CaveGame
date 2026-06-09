import unittest

from live_gibberish.asr import WordResult
from live_gibberish.filtering import WhitelistChecker, normalize_word


class FilteringTests(unittest.TestCase):
    def test_normalize_word_strips_edge_punctuation(self):
        self.assertEqual(normalize_word(" Hello! "), "hello")
        self.assertEqual(normalize_word("'Cave'"), "cave")

    def test_checker_allows_only_confident_whitelisted_words(self):
        checker = WhitelistChecker(["hello", "cave"], confidence_threshold=0.7)

        allowed = checker.check(WordResult("Hello!", 0.0, 0.1, 0.9))
        low_confidence = checker.check(WordResult("cave", 0.1, 0.2, 0.2))
        blocked = checker.check(WordResult("danger", 0.2, 0.3, 0.9))
        filler = checker.check(WordResult("um", 0.3, 0.4, 1.0))

        self.assertTrue(allowed.allowed)
        self.assertEqual(allowed.reason, "whitelist")
        self.assertFalse(low_confidence.allowed)
        self.assertEqual(low_confidence.reason, "low-confidence")
        self.assertFalse(blocked.allowed)
        self.assertEqual(blocked.reason, "not-whitelisted")
        self.assertFalse(filler.allowed)
        self.assertEqual(filler.reason, "filler")


if __name__ == "__main__":
    unittest.main()

