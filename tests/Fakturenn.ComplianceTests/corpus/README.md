# Compliance corpus

Golden files for electronic-invoice output, compared with
`NormalizingXmlComparer`.

## Layout

    corpus/
      facturx/<profile>/<case>.expected.xml
      xrechnung-cii/<profile>/<case>.expected.xml
      xrechnung-ubl/<profile>/<case>.expected.xml

## Rules

- Every file records its provenance and the exact standard version it was
  produced against, in an XML comment at the top of the file.
- A golden file is never edited to make a failing test pass. Either the
  generator is wrong, or the standard changed — and a standard change gets its
  own file under a new version directory, so the old expectation stays testable.
- Files are compared after normalization: comments, insignificant whitespace
  and attribute order are ignored. Element order is significant, because
  EN 16931 syntax bindings define ordered sequences.
- Namespace differences are detected, not ignored — an element's namespace URI
  is part of its identity to the comparer. CII and UBL golden files therefore
  must live under distinct paths (see Layout above) and are never
  interchangeable, even when their local element names coincide.
- A malformed golden file does not surface as a comparison failure. Parsing
  happens before normalization, so a syntax error in a `.expected.xml` file
  throws `System.Xml.XmlException` (with line and position) out of
  `NormalizingXmlComparer.Compare`, rather than being reported as a mismatch.

## Status

Empty. No electronic-invoice generator exists until epic E12. The comparer and
its tests ship first so that the corpus has something trustworthy to be checked
with when the generator arrives.

Planned coverage, from `docs/testing/TEST-STRATEGY.md`: Factur-X/ZUGFeRD,
XRechnung CII, XRechnung UBL if supported, multiple tax cases, references,
allowances and charges, corrections, service periods, and rounding edges.
