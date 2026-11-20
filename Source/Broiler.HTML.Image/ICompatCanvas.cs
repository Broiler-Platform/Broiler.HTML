namespace Broiler.HTML.Image;

internal interface ICompatCanvas
{
    void Save();

    void Restore();

    void Translate(float x, float y);
}
