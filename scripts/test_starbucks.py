"""Tests for the Starbucks brand script. Run: python scripts/test_starbucks.py"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "brands"))
import starbucks  # noqa: E402

SAMPLE = (
    "<ul class='store_list'>"
    "<li id='store_data_285' class='store_data' onMouseover='fetchStoreMapLocation(285,25.040795,121.564725);'>"
    "<div id='store_div_285' class='text_block'><h4 class='store_name'>台北時代門市</h4>"
    "<p class='store_add'>台北市信義區忠孝東路五段8號2樓</p></div></li>"
    "<li id='store_data_575' class='store_data' onMouseover='fetchStoreMapLocation(575,25.040665,121.565443);'>"
    "<div id='store_div_575' class='text_block'><h4 class='store_name'>時代B2門市</h4>"
    "<p class='store_add'>臺中市西屯區台灣大道三段 (無糕點加熱服務)</p></div></li>"
    "<li id='store_data_9' class='store_data' onMouseover='fetchStoreMapLocation(9,0,0);'>"
    "<div class='text_block'><h4 class='store_name'>A &amp; B門市</h4><p class='store_add'>金門縣金城鎮民生路1號</p></div></li>"
    "</ul>"
)


class Parse(unittest.TestCase):
    def test_reads_name_address_and_coordinates(self):
        stores = list(starbucks.parse(SAMPLE))
        self.assertEqual(len(stores), 3)
        self.assertEqual(stores[0], {"name": "台北時代門市", "address": "台北市信義區忠孝東路五段8號2樓",
                                     "lat": 25.040795, "lng": 121.564725})

    def test_unescapes_html(self):
        self.assertEqual(list(starbucks.parse(SAMPLE))[2]["name"], "A & B門市")


class Build(unittest.TestCase):
    def test_open_network_brand_without_passwords(self):
        brand = starbucks.build(starbucks.parse(SAMPLE))
        self.assertEqual(brand["security"], "open")
        self.assertEqual(brand["ssids"], ["Starbucks Wi-Fi"])
        self.assertTrue(all(s["passwords"] == [] for s in brand["stores"]))

    def test_city_from_address_and_bad_coordinates_zeroed(self):
        stores = {s["name"]: s for s in starbucks.build(starbucks.parse(SAMPLE))["stores"]}
        self.assertEqual(stores["時代B2門市"]["city"], "台中市")
        self.assertEqual(stores["A & B門市"]["city"], "金門縣")
        self.assertEqual((stores["A & B門市"]["lat"], stores["A & B門市"]["lng"]), (0.0, 0.0))


if __name__ == "__main__":
    unittest.main()
