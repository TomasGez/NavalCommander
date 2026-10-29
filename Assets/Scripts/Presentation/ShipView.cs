using System.Collections.Generic;
using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: el dibujo de UN barco. Arma el visual (tu prefab o cuadrados por defecto),
    /// pinta solo el cuerpo y lo coloca, escala y rota. No sabe nada de red.
    /// </summary>
    public class ShipView : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _bodyRenderers = new List<SpriteRenderer>();
        private string[] _untintedNameParts = new string[0];
        private float _unitSize = 1f; // lado del cuerpo en unidades de mundo con escala 1

        /// <summary>Crea el visual. Llamar una sola vez, con el objeto recién creado en el origen.</summary>
        public void Build(GameObject prefab, string[] untintedNameParts)
        {
            _untintedNameParts = untintedNameParts ?? new string[0];

            if (prefab != null) BuildFromPrefab(prefab);
            else BuildDefaultShape();
        }

        public void SetBodyColor(Color color)
        {
            foreach (var renderer in _bodyRenderers)
                if (renderer != null) renderer.color = color;
        }

        /// <summary>Coloca el barco: centro en mundo, lado del cuerpo en mundo y dirección (0 = Norte, 1 = Este...).</summary>
        public void Place(Vector3 center, float bodySizeInWorld, int facing)
        {
            transform.localScale = Vector3.one * (bodySizeInWorld / _unitSize);
            transform.position = center;
            transform.rotation = Quaternion.Euler(0f, 0f, -90f * (facing % 4)); // Norte = +Y; Este = derecha
        }

        // ---------------- Armado ----------------

        private void BuildFromPrefab(GameObject prefab)
        {
            var view = Instantiate(prefab, transform);
            Bounds bodyBounds = default, allBounds = default;
            bool hasBody = false, hasAny = false;

            foreach (var renderer in view.GetComponentsInChildren<SpriteRenderer>())
            {
                bool untinted = IsUntinted(renderer);
                renderer.sortingOrder = untinted ? 11 : 10; // el triángulo siempre encima del cuerpo
                Grow(ref allBounds, ref hasAny, renderer.bounds);
                if (!untinted)
                {
                    _bodyRenderers.Add(renderer);
                    Grow(ref bodyBounds, ref hasBody, renderer.bounds);
                }
            }

            // Se centra y se mide el CUERPO (no el triángulo): el cuadrado queda justo en el medio de la celda.
            if (!hasBody && !hasAny) return;
            Bounds reference = hasBody ? bodyBounds : allBounds;
            view.transform.position -= reference.center;
            _unitSize = Mathf.Max(0.0001f, Mathf.Max(reference.size.x, reference.size.y));
        }

        private void BuildDefaultShape()
        {
            var body = Primitives2D.CreateSquare("Cuerpo", transform, Color.white, 10);
            body.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            _bodyRenderers.Add(body.GetComponent<SpriteRenderer>());

            var nose = Primitives2D.CreateSquare("Proa", transform, Color.red, 11);
            nose.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            nose.transform.localScale = new Vector3(0.3f, 0.3f, 1f);

            _unitSize = 0.8f;
        }

        private bool IsUntinted(SpriteRenderer renderer)
        {
            string objectName = renderer.gameObject.name.ToLowerInvariant();
            foreach (var part in _untintedNameParts)
                if (!string.IsNullOrEmpty(part) && objectName.Contains(part)) return true;
            return false;
        }

        private static void Grow(ref Bounds bounds, ref bool initialized, Bounds other)
        {
            if (!initialized) { bounds = other; initialized = true; }
            else bounds.Encapsulate(other);
        }
    }
}
