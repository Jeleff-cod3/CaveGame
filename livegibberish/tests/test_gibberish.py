import unittest

from live_gibberish.gibberish import GibberishMapper, estimate_syllable_count


class GibberishTests(unittest.TestCase):
    def test_map_word_is_deterministic_for_seed(self):
        mapper = GibberishMapper(seed="secret")

        first = mapper.map_word("Danger!")
        second = mapper.map_word("danger")

        self.assertEqual(first.text, second.text)
        self.assertEqual(first.normalized_word, "danger")
        self.assertNotEqual(first.text, "danger")

    def test_map_word_changes_with_seed(self):
        first = GibberishMapper(seed="one").map_word("danger")
        second = GibberishMapper(seed="two").map_word("danger")

        self.assertNotEqual(first.text, second.text)

    def test_syllable_count_is_bounded(self):
        self.assertEqual(estimate_syllable_count(""), 1)
        self.assertGreaterEqual(estimate_syllable_count("internationalization"), 1)
        self.assertLessEqual(estimate_syllable_count("internationalization"), 4)


if __name__ == "__main__":
    unittest.main()

