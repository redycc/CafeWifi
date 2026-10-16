"""Tests for the Louisa brand script. Run: python scripts/test_louisa.py"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "brands"))
import louisa  # noqa: E402


class Passwords(unittest.TestCase):
    def test_taipei_drops_area_code_first(self):
        self.assertEqual(louisa.passwords("02-2361-0718"), ["23610718", "0223610718", "223610718"])

    def test_ten_digit_drops_leading_zero_first(self):
        self.assertEqual(louisa.passwords("04-2326-2617"), ["423262617", "0423262617", "23262617"])

    def test_nine_digit_has_two_candidates(self):
        self.assertEqual(louisa.passwords("03-338-0980"), ["33380980", "033380980"])

    def test_extension_is_ignored(self):
        self.assertEqual(louisa.passwords("02-2345-8613#12")[0], "23458613")

    def test_empty_phone(self):
        self.assertEqual(louisa.passwords(""), [])


class City(unittest.TestCase):
    def test_first_address_part_with_tai_normalized(self):
        self.assertEqual(louisa.city("臺北市 中正區 仁愛路一段17號"), "台北市")

    def test_missing_address(self):
        self.assertEqual(louisa.city(""), "其他")


if __name__ == "__main__":
    unittest.main()
