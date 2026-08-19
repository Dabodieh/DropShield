<?php

declare(strict_types=1);

namespace DropShield\Connector\Controller\Adminhtml\Drop;

use DropShield\Connector\Model\ProtectedDropRepository;
use Magento\Backend\App\Action;
use Magento\Framework\Exception\LocalizedException;

class Save extends Action
{
    public const ADMIN_RESOURCE = 'DropShield_Connector::protected_drops';

    public function __construct(Action\Context $context, private readonly ProtectedDropRepository $repository)
    {
        parent::__construct($context);
    }

    public function execute()
    {
        if (!$this->getRequest()->isPost()) {
            return $this->_redirect('*/*/index');
        }

        try {
            $id = $this->getRequest()->getParam('id');
            $productIds = (array) $this->getRequest()->getParam('product_ids', []);
            $removeProductIds = array_map('intval', (array) $this->getRequest()->getParam('remove_product_ids', []));
            $savedId = $this->repository->save(
                $id === null || $id === '' ? null : (int) $id,
                (string) $this->getRequest()->getParam('drop_identifier'),
                (string) $this->getRequest()->getParam('name'),
                (bool) $this->getRequest()->getParam('is_enabled'),
                array_values(array_diff(array_map('intval', $productIds), $removeProductIds))
            );
            $this->messageManager->addSuccessMessage(__('Protected drop saved.'));
            return $this->_redirect('*/*/edit', ['id' => $savedId]);
        } catch (LocalizedException $exception) {
            $this->messageManager->addErrorMessage($exception->getMessage());
        } catch (\Throwable $exception) {
            $this->messageManager->addErrorMessage(__('The protected drop could not be saved.'));
        }

        return $this->_redirect('*/*/edit', ['id' => (int) $this->getRequest()->getParam('id')]);
    }
}
